using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Builds the command-line argument list for launching a Chromium-based browser.
/// </summary>
internal static class ChromiumArgs
{
    private static readonly string[] DefaultArgs =
    [
        "--disable-background-networking",
        "--disable-client-side-phishing-detection",
        "--disable-default-apps",
        "--disable-extensions",
        "--disable-hang-monitor",
        "--disable-popup-blocking",
        "--disable-prompt-on-repost",
        "--disable-sync",
        "--disable-translate",
        "--metrics-recording-only",
        "--no-first-run",
        "--safebrowsing-disable-auto-update"
    ];

    internal static List<string> Build(LaunchOptions options, int debuggingPort, string userDataDir)
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

        args.Add($"--remote-debugging-port={debuggingPort}");
        args.Add($"--user-data-dir={userDataDir}");

        if (options.Headless)
        {
            args.Add("--headless=new");
        }
        else
        {
            args.Add("--disable-blink-features=AutomationControlled");
        }

        if (options.DownloadsPath is not null)
            args.Add($"--download-default-directory={options.DownloadsPath}");

        if (options.Args is not null)
        {
            foreach (var arg in options.Args)
                args.Add(arg);
        }

        return args;
    }
}
