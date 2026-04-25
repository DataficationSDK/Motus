namespace Motus.Abstractions;

/// <summary>
/// Options for launching a browser instance.
/// </summary>
public sealed record LaunchOptions
{
    /// <summary>Whether to run the browser in headless mode.</summary>
    public bool Headless { get; init; } = true;

    /// <summary>The browser distribution channel to use.</summary>
    public BrowserChannel? Channel { get; init; }

    /// <summary>Path to a browser executable to use instead of the bundled one.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Additional command-line arguments to pass to the browser.</summary>
    public IReadOnlyList<string>? Args { get; init; }

    /// <summary>Slows down operations by the specified number of milliseconds.</summary>
    public int SlowMo { get; init; }

    /// <summary>Maximum time in milliseconds to wait for the browser to start.</summary>
    public int Timeout { get; init; } = 30_000;

    /// <summary>Path to a user data directory for the browser profile.</summary>
    public string? UserDataDir { get; init; }

    /// <summary>Whether to handle the SIGINT signal.</summary>
    public bool HandleSIGINT { get; init; } = true;

    /// <summary>Whether to handle the SIGTERM signal.</summary>
    public bool HandleSIGTERM { get; init; } = true;

    /// <summary>If specified, default arguments that should be filtered out.</summary>
    public IReadOnlyList<string>? IgnoreDefaultArgs { get; init; }

    /// <summary>Path to a folder for browser downloads.</summary>
    public string? DownloadsPath { get; init; }

    /// <summary>Manually registered plugins to load into each browser context.</summary>
    public IReadOnlyList<IPlugin>? Plugins { get; init; }

    /// <summary>Accessibility audit hook configuration. Disabled by default.</summary>
    public AccessibilityOptions? Accessibility { get; init; }

    /// <summary>Performance metrics collector configuration. Disabled by default.</summary>
    public PerformanceOptions? Performance { get; init; }

    /// <summary>Code coverage collector configuration. Disabled by default.</summary>
    public CoverageOptions? Coverage { get; init; }
}
