namespace Motus.Cli.Commands;

public static class BrowserPathHelper
{
    private static readonly string BrowserCacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".motus", "browsers");

    private static readonly string LegacyMarkerPath = Path.Combine(BrowserCacheDir, ".installed");

    public static string? Resolve(string? channel = null)
    {
        // Try channel-specific marker first
        if (channel is not null)
        {
            var channelMarker = Path.Combine(BrowserCacheDir, $".installed.{channel}");
            var channelPath = ReadMarker(channelMarker);
            if (channelPath is not null)
                return channelPath;
        }

        // Fall back to legacy single marker for backward compatibility
        return ReadMarker(LegacyMarkerPath);
    }

    private static string? ReadMarker(string markerPath)
    {
        if (!File.Exists(markerPath))
            return null;

        var path = File.ReadAllText(markerPath).Trim();
        return File.Exists(path) ? path : null;
    }
}
