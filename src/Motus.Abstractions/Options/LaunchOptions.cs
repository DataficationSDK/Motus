namespace Motus.Abstractions;

/// <summary>
/// Options for launching a browser instance.
/// </summary>
public sealed record LaunchOptions
{
    /// <summary>Whether to run the browser in headless mode.</summary>
    public bool? Headless { get; init; }

    /// <summary>The browser distribution channel to use.</summary>
    public BrowserChannel? Channel { get; init; }

    /// <summary>Path to a browser executable to use instead of the bundled one.</summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Additional command-line arguments to pass to the browser.</summary>
    public IEnumerable<string>? Args { get; init; }

    /// <summary>Slows down operations by the specified number of milliseconds.</summary>
    public double? SlowMo { get; init; }

    /// <summary>Maximum time in milliseconds to wait for the browser to start.</summary>
    public double? Timeout { get; init; }

    /// <summary>Path to a user data directory for the browser profile.</summary>
    public string? UserDataDir { get; init; }

    /// <summary>Whether to auto-download the browser if not found.</summary>
    public bool? HandleSIGINT { get; init; }

    /// <summary>Whether to pipe browser process stderr to the parent process.</summary>
    public bool? HandleSIGTERM { get; init; }

    /// <summary>Proxy settings for all browser contexts.</summary>
    public ProxySettings? Proxy { get; init; }

    /// <summary>Environment variables to set for the browser process.</summary>
    public IDictionary<string, string>? Env { get; init; }
}
