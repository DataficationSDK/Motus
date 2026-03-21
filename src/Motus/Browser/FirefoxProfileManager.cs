namespace Motus;

/// <summary>
/// Creates and configures Firefox profile directories for WebDriver BiDi.
/// </summary>
internal static class FirefoxProfileManager
{
    private const string UserJsContent = """
        user_pref("remote.active-protocols", 2);
        user_pref("remote.enabled", true);
        user_pref("remote.allow-hosts", "127.0.0.1");
        """;

    /// <summary>
    /// Creates a temporary Firefox profile directory with the required BiDi preferences.
    /// </summary>
    /// <returns>The profile directory path and whether we own the temp directory.</returns>
    internal static (string ProfileDir, bool OwnsTempDir) CreateTempProfile(string? userDataDir)
    {
        if (userDataDir is not null)
        {
            // User supplied their own profile directory; do not overwrite user.js
            Directory.CreateDirectory(userDataDir);
            return (userDataDir, false);
        }

        var profileDir = Path.Combine(
            Path.GetTempPath(),
            "motus-firefox-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(profileDir);

        File.WriteAllText(Path.Combine(profileDir, "user.js"), UserJsContent);

        return (profileDir, true);
    }
}
