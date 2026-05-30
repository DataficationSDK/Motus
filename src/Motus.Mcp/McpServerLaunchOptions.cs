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

    /// <summary>Maps these options onto the browser launch options.</summary>
    internal LaunchOptions ToLaunchOptions() => new()
    {
        Headless = Headless,
        ExecutablePath = ExecutablePath,
        Channel = Channel,
    };
}
