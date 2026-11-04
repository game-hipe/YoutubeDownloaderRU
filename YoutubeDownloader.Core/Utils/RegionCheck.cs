using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using YoutubeExplode;

namespace YoutubeDownloader.Core.Utils;

/// <summary>
/// Neutralizes the region-based blocking that was added to the upstream
/// YoutubeExplode library. The library checks the user's OS locale/region
/// settings and refuses to work for users whose system is configured for
/// certain countries, showing a political message instead. This check has
/// nothing to do with YouTube functionality, so this class disables it
/// unconditionally on every platform, regardless of the user's location.
/// </summary>
public static class RegionCheck
{
    private static readonly Regex BypassableTypePattern = new(
        @"Region|Geopolit|Patriot|Country|Geo\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static bool _disabled;

    /// <summary>
    /// Disables the upstream YoutubeExplode region check by:
    /// 1. Setting the well-known bypass environment variable (official escape hatch).
    /// 2. Creating the well-known bypass marker file in the working directory.
    /// 3. Reflectively forcing any internal "IsBypassed"/"IsRestricted"-style
    ///    properties in the YoutubeExplode assembly to safe values, so the check
    ///    is neutralized even if the bypass constants change in future versions.
    /// </summary>
    public static void Disable()
    {
        if (_disabled)
            return;
        _disabled = true;

        try
        {
            // 1. Official env-var escape hatch used by the library itself
            Environment.SetEnvironmentVariable("SLAVA_UKRAINI", "1");

            // 2. Official file-based escape hatch used by the library itself
            var markerFile = Path.Combine(Directory.GetCurrentDirectory(), "SLAVA_UKRAINI");
            if (!File.Exists(markerFile))
                File.Create(markerFile).Dispose();

            // 3. Defensive reflection pass over the loaded YoutubeExplode assembly
            ForceBypassFlags();
        }
        catch
        {
            // Never crash the app because of the neutralization itself;
            // steps 1-2 already cover the known versions of the check.
        }
    }

    private static void ForceBypassFlags()
    {
        var asm = typeof(YoutubeClient).Assembly;

        var candidates = asm
            .GetTypes()
            .Where(t =>
                BypassableTypePattern.IsMatch(t.Name)
                || t.FullName?.IndexOf("Region", StringComparison.OrdinalIgnoreCase) >= 0
            );

        foreach (var type in candidates)
        {
            // Properties like "IsBypassed" -> force true
            SetProperties(type, "IsBypassed", true);
            // Properties like "IsRestricted" / "IsInRestrictedRegion" -> force false
            SetProperties(type, "Restricted", false);
        }

        // Also scan all types for these specifically-named static properties,
        // in case the owning type name doesn't match the pattern above.
        foreach (var type in asm.GetTypes())
        {
            SetProperties(type, "IsBypassed", true);
            SetProperties(type, "IsRestricted", false);
        }
    }

    private static void SetProperties(Type type, string nameContains, object value)
    {
        PropertyInfo[] props;
        try
        {
            props = type.GetProperties(
                BindingFlags.Public
                    | BindingFlags.NonPublic
                    | BindingFlags.Static
                    | BindingFlags.Instance
            );
        }
        catch
        {
            return;
        }

        foreach (
            var prop in props.Where(p => p.Name.Contains(nameContains, StringComparison.Ordinal))
        )
        {
            try
            {
                if (prop.PropertyType != typeof(bool) || !prop.CanWrite)
                    continue;

                var setter = prop.GetSetMethod(true);
                if (setter?.IsStatic == true)
                    setter.Invoke(null, [value]);
                else if (prop.IsStatic())
                    prop.SetValue(null, value);
            }
            catch
            {
                // Best-effort only
            }
        }
    }
}

file static class ReflectionExtensions
{
    public static bool IsStatic(this PropertyInfo prop) =>
        prop.GetGetMethod(true)?.IsStatic == true || prop.GetSetMethod(true)?.IsStatic == true;
}
