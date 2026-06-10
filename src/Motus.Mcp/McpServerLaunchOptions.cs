using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Configuration for the browser that the MCP server drives. These values are
/// supplied by the host (for example a CLI subcommand) and mapped onto the
/// browser launch options when the session first needs a page.
/// </summary>
public sealed record McpServerLaunchOptions
{
    /// <summary>Whether the browser runs without a visible window. Defaults to true.</summary>
    public bool Headless { get; init; } = true;

    /// <summary>
    /// An explicit path to the browser executable. When set, it takes precedence
    /// over <see cref="Channel"/>. The host resolves this (for example from a
    /// managed browser cache) and passes it through.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>
    /// The browser channel to launch when no <see cref="ExecutablePath"/> is given.
    /// Left null, the framework auto-detects an installed browser.
    /// </summary>
    public BrowserChannel? Channel { get; init; }

    /// <summary>
    /// The viewport applied to every context the session creates. Defaults to
    /// 1280x800; small browser-default viewports push real application chrome
    /// off screen. The resize tool can change it per page at runtime.
    /// </summary>
    public ViewportSize Viewport { get; init; } = new(1280, 800);

    /// <summary>
    /// When set, every context the session creates records video into this
    /// directory, one file per page, finalized when the page closes. Recording
    /// binds at context creation, so a session recorded this way cannot also
    /// use the on-demand video tools.
    /// </summary>
    public string? RecordVideoDir { get; init; }

    /// <summary>Maps these options onto the browser launch options.</summary>
    internal LaunchOptions ToLaunchOptions() => new()
    {
        Headless = Headless,
        ExecutablePath = ExecutablePath,
        Channel = Channel,
        // Viewport emulation only controls the CSS viewport; a headed window
        // that is smaller would clip it, so size the window to match.
        Args = Headless ? null : [$"--window-size={Viewport.Width},{Viewport.Height}"],
        // Performance telemetry is collected for every session: the observer is
        // injected at page creation and metrics are gathered after each navigation,
        // so get_performance has data to return. The overhead is negligible.
        Performance = new PerformanceOptions { Enable = true },
    };

    /// <summary>Maps these options onto the context options for new contexts.</summary>
    internal ContextOptions ToContextOptions() => new()
    {
        Viewport = Viewport,
        RecordVideo = RecordVideoDir is null
            ? null
            // Record at the viewport size; the library's own default would
            // scale the capture down.
            : new RecordVideoOptions { Dir = RecordVideoDir, Size = Viewport },
    };
}
