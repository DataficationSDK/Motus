using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Builds the command-line argument list for launching Firefox with WebDriver BiDi.
/// </summary>
internal static class FirefoxArgs
{
    private static readonly string[] DefaultArgs =
    [
        "-no-remote",
        "-wait-for-browser",
        "--new-instance"
    ];

    internal static (List<string> Args, Dictionary<string, string> EnvironmentVars) Build(
        LaunchOptions options, int debuggingPort, string profileDir)
    {
        var ignoreSet = options.IgnoreDefaultArgs is not null
            ? new HashSet<string>(options.IgnoreDefaultArgs, StringComparer.Ordinal)
            : null;

        var args = new List<string>();

        foreach (var arg in DefaultArgs)
        {
            if (ignoreSet is null || !ignoreSet.Contains(arg))
                args.Add(arg);
        }

        args.Add("--remote-debugging-port");
        args.Add(debuggingPort.ToString());
        args.Add("-profile");
        args.Add(profileDir);

        if (options.Headless)
            args.Add("--headless");

        if (options.Args is not null)
        {
            foreach (var arg in options.Args)
                args.Add(arg);
        }

        var envVars = new Dictionary<string, string>();
        if (options.Headless)
            envVars["MOZ_HEADLESS"] = "1";

        return (args, envVars);
    }
}
