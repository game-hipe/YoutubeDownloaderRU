using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace YoutubeDownloader.Core.Utils;

/// <summary>
/// Wraps the underlying <see cref="HttpClient"/> and makes sure that no request
/// or cookie sent to YouTube carries any regional hints (such as "gl=RU" or
/// "hl=ru"). This guarantees identical behavior for all users regardless of
/// where they are located.
/// </summary>
public class NeutralHttpClient(HttpClient inner) : DelegatingHandler(inner, disposeInner: false)
{
    private static readonly string[] RestrictedCountryCodes = ["RU", "BY"];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        SanitizeUri(request);
        SanitizeCookies(request);

        var response = await base.SendAsync(request, cancellationToken);

        // If YouTube rejects the request specifically because of the inferred
        // user location, retry once with an explicitly neutral region cookie.
        if (IsGeoBlocked(response))
        {
            DisposeContent(response);

            using var retry = CloneRequest(request);
            SetNeutralGeoCookies(retry);

            response = await base.SendAsync(retry, cancellationToken);
        }

        return response;
    }

    private static void SanitizeUri(HttpRequestMessage request)
    {
        var uri = request.RequestUri;
        if (uri is null || string.IsNullOrEmpty(uri.Query))
            return;

        var changed = false;
        var parts = new List<string>();

        foreach (var segment in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var key = segment.Split('=', 2)[0];

            // Drop country/language hints so YouTube infers nothing from us
            if (string.Equals(key, "gl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(key, "hl", StringComparison.OrdinalIgnoreCase))
            {
                changed = true;
                continue;
            }

            // Drop explicit restricted-country values in other parameters
            if (
                segment.Contains("=-ru", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("-ru&", StringComparison.OrdinalIgnoreCase)
                || segment.EndsWith("-ru", StringComparison.OrdinalIgnoreCase)
                || segment.Contains("=RU", StringComparison.Ordinal)
            )
            {
                changed = true;
                continue;
            }

            parts.Add(segment);
        }

        if (!changed)
            return;

        var query = parts.Count > 0 ? "?" + string.Join("&", parts) : "";
        request.RequestUri = new UriBuilder(uri) { Query = query }.Uri;
    }

    private static void SanitizeCookies(HttpRequestMessage request)
    {
        if (!request.Headers.TryGetValues("Cookie", out var values))
            return;

        var cleaned = values
            .SelectMany(v => v.Split(';', StringSplitOptions.RemoveEmptyEntries))
            .Select(c => c.Trim())
            .Where(c => !IsRestrictedCookie(c))
            .ToList();

        request.Headers.Remove("Cookie");

        if (cleaned.Count > 0)
            request.Headers.Add("Cookie", string.Join("; ", cleaned));
    }

    private static bool IsRestrictedCookie(string cookie)
    {
        var name = cookie.Split('=', 2)[0].Trim();

        if (
            name.Equals("gl", StringComparison.OrdinalIgnoreCase)
            || name.Equals("hl", StringComparison.OrdinalIgnoreCase)
            || name.Equals("prefs", StringComparison.OrdinalIgnoreCase)
            || name.Equals("SOCS", StringComparison.OrdinalIgnoreCase)
        )
            return true;

        var value = cookie.Split('=', 2).Length > 1 ? cookie.Split('=', 2)[1] : "";

        return RestrictedCountryCodes.Any(
            code =>
                value.Contains(code, StringComparison.OrdinalIgnoreCase)
                && !value.Contains("UKR", StringComparison.OrdinalIgnoreCase)
        );
    }

    private static void SetNeutralGeoCookies(HttpRequestMessage request)
    {
        request.Headers.Remove("Cookie");
        // "gl=US" is the most widely accepted neutral region hint on YouTube
        request.Headers.Add("Cookie", "gl=US&has_verified=1");
    }

    private static bool IsGeoBlocked(HttpResponseMessage response)
    {
        try
        {
            var text = response.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";

            return text.Contains("not available in your country", StringComparison.OrdinalIgnoreCase)
                || text.Contains(
                    "Video unavailable.*country",
                    StringComparison.OrdinalIgnoreCase
                );
        }
        catch
        {
            return false;
        }
    }

    private static void DisposeContent(HttpResponseMessage response)
    {
        try
        {
            response.Content?.Dispose();
            response.Dispose();
        }
        catch
        {
            // Ignore
        }
    }

    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri)
        {
            Version = request.Version,
        };

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (request.Content is not null)
        {
            var bytes = request.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
            clone.Content = new ByteArrayContent(bytes);

            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }
}
