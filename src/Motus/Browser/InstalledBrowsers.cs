using Motus.Abstractions;

namespace Motus;

/// <summary>
/// The browser cache <c>motus install</c> downloads into, and the marker files that record what it
/// installed there. The installer writes the markers and the launcher reads them, so both go
/// through here rather than each keeping its own idea of where the cache is and what the markers
/// are called.
/// </summary>
/// <remarks>
/// A download is not found by looking for an executable name under the cache. Each build unpacks
/// into a layout of its own, <c>chromium-{version}/chrome-{platform}/chrome</c> on Linux,
/// <c>chrome.exe</c> in the same place on Windows and an application bundle on macOS, and several
/// versions can sit side by side. The installer records the exact executable it installed in
/// <c>.installed.{channel}</c>, and that marker is the answer.
/// </remarks>
internal static class InstalledBrowsers
{
    /// <summary>
    /// The cache <c>motus install</c> uses when it is not given <c>--path</c>:
    /// <c>~/.motus/browsers</c>.
    /// </summary>
    internal static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".motus", "browsers");

    /// <summary>
    /// The marker that records the executable installed for a channel, such as
    /// <c>.installed.chromium</c>.
    /// </summary>
    internal static string MarkerPath(string root, string channel) =>
        Path.Combine(root, $".installed.{channel.ToLowerInvariant()}");

    /// <summary>
    /// The marker older installs wrote, which records the last browser installed without recording
    /// which browser that was.
    /// </summary>
    internal static string LegacyMarkerPath(string root) => Path.Combine(root, ".installed");

    /// <summary>
    /// The executable <c>motus install</c> recorded for a channel under <paramref name="root"/>, or
    /// null when there is no marker for it or the marker names a file that is no longer there.
    /// </summary>
    internal static string? Find(string root, BrowserChannel channel) =>
        ReadMarker(MarkerPath(root, channel.ToString()));

    /// <summary>
    /// The executable a marker names, or null when the marker is missing, cannot be read, or names a
    /// file that no longer exists.
    /// </summary>
    /// <remarks>
    /// A marker outlives the download it describes when the cache is cleaned by hand, so a stale
    /// one is treated as no marker at all and the caller falls through to whatever else it would
    /// have tried. A marker that cannot be read is treated the same way: it is a hint about where a
    /// browser might be, and failing a launch over it would be worse than not having it.
    /// </remarks>
    internal static string? ReadMarker(string markerPath)
    {
        string path;
        try
        {
            if (!File.Exists(markerPath))
                return null;

            path = File.ReadAllText(markerPath).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return path.Length > 0 && File.Exists(path) ? path : null;
    }
}
