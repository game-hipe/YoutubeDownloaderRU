using System;
using System.Globalization;
using System.IO;
using System.Threading;

namespace YoutubeDownloader.Core.Utils;

/// <summary>
/// Neutralizes the region-based blocking that was added to the upstream
/// YoutubeExplode package (6.5.x+). The library reads the user's OS region
/// settings (and in some versions the Windows registry key
/// "Control Panel\International\Geo\Nation") and throws an exception with a
/// political message for users whose system is configured for certain
/// countries. That check has nothing to do with YouTube functionality, so this
/// class disables it unconditionally on every platform, regardless of where
/// the user is located.
/// </summary>
public static class RegionCheck
{
    private static int _disabled;

    /// <summary>
    /// Disables the upstream YoutubeExplode region check by:
    /// 1. Setting the bypass environment variable that the library itself
    ///    recognizes as its official escape hatch.
    /// 2. Creating the bypass marker file that the library itself checks for
    ///    in the current working directory.
    /// 3. Forcing the current culture's region to a neutral value so that the
    ///    locale-based detection used by the library never matches.
    /// Must be called before the first request is made through YoutubeClient.
    /// </summary>
    public static void Disable()
    {
        if (Interlocked.Exchange(ref _disabled, 1) != 0)
            return;

        try
        {
            // 1. Official env-var escape hatch recognized by the library
            Environment.SetEnvironmentVariable("SLAVA_UKRAINI", "1");

            // 2. Official file-based escape hatch recognized by the library
            var markerFilePath = Path.Combine(Directory.GetCurrentDirectory(), "SLAVA_UKRAINI");

            if (!File.Exists(markerFilePath))
                File.Create(markerFilePath).Dispose();
        }
        catch
        {
            // Working directory may be read-only in some deployments.
            // Step 1 still covers those cases.
        }

        try
        {
            // 3. Neutralize locale-based region detection process-wide.
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
        }
        catch
        {
            // Best-effort only; steps 1-2 already cover all known versions.
        }
    }
}
