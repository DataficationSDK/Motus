namespace Motus.Cli.Commands;

public static class BrowserPathHelper
{
    // The cache and its marker names are shared with the launcher, which reads the same markers
    // when no executable path is given, so the CLI and a test run agree on what was installed.
    private static readonly string BrowserCacheDir = InstalledBrowsers.DefaultRoot;

    private static readonly string LegacyMarkerPath = InstalledBrowsers.LegacyMarkerPath(BrowserCacheDir);

    public static string? Resolve(string? channel = null)
    {
        // Try channel-specific marker first
        if (channel is not null)
        {
            var channelPath = InstalledBrowsers.ReadMarker(InstalledBrowsers.MarkerPath(BrowserCacheDir, channel));
            if (channelPath is not null)
                return channelPath;
        }

        // Fall back to legacy single marker for backward compatibility
        return InstalledBrowsers.ReadMarker(LegacyMarkerPath);
    }

    /// <summary>
    /// Resolves only the browser installed for the given channel, with no fall back to the legacy
    /// marker. The legacy marker records a path without recording which browser it points at, so a
    /// caller that asked for a particular channel cannot be answered from it.
    /// </summary>
    public static string? ResolveChannel(string channel)
    {
        ArgumentException.ThrowIfNullOrEmpty(channel);
        return InstalledBrowsers.ReadMarker(InstalledBrowsers.MarkerPath(BrowserCacheDir, channel));
    }
}
