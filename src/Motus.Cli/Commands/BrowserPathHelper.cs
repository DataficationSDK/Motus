namespace Motus.Cli.Commands;

public static class BrowserPathHelper
{
    private static readonly string MarkerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".motus", "browsers", ".installed");

    public static string? Resolve()
    {
        if (!File.Exists(MarkerPath))
            return null;

        var path = File.ReadAllText(MarkerPath).Trim();
        return File.Exists(path) ? path : null;
    }
}
