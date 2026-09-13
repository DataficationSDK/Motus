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

    /// <summary>
    /// Builds the browser's command line.
    /// </summary>
    /// <param name="debuggingPort">
    /// The port to open the debugging endpoint on, or null to drive the browser over a pipe
    /// instead. A pipe is preferred where it can be arranged, because the browser exits when its
    /// end closes and so cannot outlive the process that started it.
    /// </param>
    internal static List<string> Build(LaunchOptions options, string userDataDir, int? debuggingPort = null)
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

        args.Add(debuggingPort is { } port
            ? $"--remote-debugging-port={port}"
            : "--remote-debugging-pipe");

        args.Add($"--user-data-dir={userDataDir}");

        if (options.Headless)
        {
            args.Add("--headless=new");
        }
        else
        {
            args.Add("--no-startup-window");
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
