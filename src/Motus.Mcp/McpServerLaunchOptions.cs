using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Configuration for the browser that the MCP server drives. These values are
/// supplied by the host (for example a CLI subcommand) and mapped onto the
/// browser launch options when the session first needs a page.
/// </summary>
public sealed record McpServerLaunchOptions
{
    /// <summary>
    /// The debugging endpoint of a browser that is already running. When set, the session connects
    /// to that browser instead of starting one, and drives whatever is already open in it.
    /// </summary>
    /// <remarks>
    /// Either form of endpoint is accepted: the CDP WebSocket URL, or the HTTP debugging endpoint
    /// the browser was started with, such as <c>http://127.0.0.1:9222</c>.
    ///
    /// A browser reached this way is not owned. The options below that describe how to start a
    /// browser, and the ones that bind when a context is created, have nothing to apply to.
    /// </remarks>
    public string? Endpoint { get; init; }

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

    /// <summary>
    /// When true, every context the session creates draws an on-screen pseudo-cursor that
    /// follows the synthetic pointer, reflects the element's CSS cursor style, and shows a
    /// click effect. Synthetic input and screen capture do not show a cursor on their own, so
    /// this makes screenshots and recordings legible. Defaults to false.
    /// </summary>
    public bool ShowCursor { get; init; }

    /// <summary>
    /// When true, mouse movement follows a curved, eased, time-spaced path instead of jumping
    /// to the target. The CLI defaults this to <see cref="ShowCursor"/>. Defaults to false.
    /// </summary>
    public bool NaturalMouseMotion { get; init; }

    /// <summary>
    /// Extra command-line arguments handed to the browser the session starts. They are appended to
    /// the arguments the server sets itself, and are how a flag a particular environment needs
    /// reaches the browser: <c>--no-sandbox</c> in a container running as root, for instance.
    /// </summary>
    public IReadOnlyList<string>? BrowserArgs { get; init; }

    /// <summary>
    /// A profile directory the browser keeps its cookies, history, and signed-in sessions in, so
    /// one session picks up where the last left off instead of starting clean each time.
    /// </summary>
    public string? UserDataDir { get; init; }

    /// <summary>
    /// Cookies and local storage to seed every context the session creates with. The host reads
    /// this from wherever it keeps saved state and passes the parsed value through.
    /// </summary>
    public StorageState? StorageState { get; init; }

    /// <summary>The proxy every context the session creates sends its traffic through.</summary>
    public ProxySettings? Proxy { get; init; }

    /// <summary>The user agent string every context the session creates reports.</summary>
    public string? UserAgent { get; init; }

    /// <summary>The locale every context the session creates reports, such as <c>en-GB</c>.</summary>
    public string? Locale { get; init; }

    /// <summary>
    /// The time zone every context the session creates reports, such as <c>Europe/Berlin</c>.
    /// </summary>
    public string? TimezoneId { get; init; }

    /// <summary>
    /// How long an element action waits, in milliseconds, before giving up. Null leaves the
    /// framework default in place. Nothing holds a context-wide default for this, so the tools
    /// read it from <c>ActivePageService.ActionTimeout</c> and pass it on each call.
    /// </summary>
    public double? ActionTimeout { get; init; }

    /// <summary>
    /// How long a navigation waits, in milliseconds, before giving up. Null leaves the framework
    /// default in place. Carried to the tools the same way as <see cref="ActionTimeout"/>.
    /// </summary>
    public int? NavigationTimeout { get; init; }

    /// <summary>
    /// What becomes of a JavaScript dialog the page raises: <c>accept</c> answers it, <c>dismiss</c>
    /// cancels it, and <c>ask</c> leaves it pending for the agent to answer with a tool call.
    /// Defaults to <c>ask</c>.
    /// </summary>
    public string Dialogs { get; init; } = "ask";

    /// <summary>Maps these options onto the browser launch options.</summary>
    internal LaunchOptions ToLaunchOptions() => new()
    {
        Headless = Headless,
        ExecutablePath = ExecutablePath,
        Channel = Channel,
        Args = BuildBrowserArgs(),
        UserDataDir = UserDataDir,
        // Performance telemetry is collected for every session: the observer is
        // injected at page creation and metrics are gathered after each navigation,
        // so get_performance has data to return. The overhead is negligible.
        Performance = new PerformanceOptions { Enable = true },
    };

    /// <summary>
    /// The browser command line: the window sizing a headed session needs, then whatever the host
    /// asked for. Null when there is nothing to pass, which is what the launcher reads as "no extra
    /// arguments" rather than an empty command line.
    /// </summary>
    /// <remarks>
    /// Viewport emulation only controls the CSS viewport; a headed window that is smaller would
    /// clip it, so the window is sized to match. Host arguments come last so one of them can
    /// override that sizing.
    /// </remarks>
    private IReadOnlyList<string>? BuildBrowserArgs()
    {
        var args = new List<string>();

        if (!Headless)
            args.Add($"--window-size={Viewport.Width},{Viewport.Height}");

        if (BrowserArgs is not null)
            args.AddRange(BrowserArgs);

        return args.Count == 0 ? null : args;
    }

    /// <summary>Maps these options onto the options for connecting to a running browser.</summary>
    internal ConnectOptions ToConnectOptions() => new()
    {
        // The point of attaching is to drive what is already open, so the contexts and pages the
        // browser already has are adopted rather than ignored.
        AdoptExistingTargets = true,
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
        ShowCursor = ShowCursor,
        NaturalMouseMotion = NaturalMouseMotion,
        UserAgent = UserAgent,
        Locale = Locale,
        TimezoneId = TimezoneId,
        Proxy = Proxy,
        StorageState = StorageState,
    };

    // The boundaries the server keeps around the machine it runs on. These describe the server
    // rather than the browser, so they are read by SecurityPolicy rather than mapped onto any of
    // the option records above.

    /// <summary>
    /// Where tools that write a file put it. A path a tool is given is resolved inside this
    /// directory and may not escape it. Left null, a directory for this run is named under the
    /// system temporary directory and created by the first write.
    /// </summary>
    public string? OutputDirectory { get; init; }

    /// <summary>
    /// When true, tools read and write anywhere on the machine and may open <c>file:</c> URLs.
    /// Defaults to false: an agent acts partly on instructions that came from the pages it visited,
    /// so the machine it runs on is not open to it by default.
    /// </summary>
    public bool AllowUnrestrictedFileAccess { get; init; }

    /// <summary>
    /// When true, the attach tool may point the session at a browser that is already running.
    /// Defaults to false, and is implied by <see cref="Endpoint"/>: choosing a browser that holds
    /// somebody's signed-in sessions is the operator's decision rather than the agent's.
    /// </summary>
    public bool AllowAttach { get; init; }
}
