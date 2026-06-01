using Motus.Mcp;

namespace Motus.Mcp.Http;

/// <summary>
/// Configuration for the Streamable HTTP MCP host: where it binds, how it is protected, and the
/// browser options each session launches with.
/// </summary>
public sealed record McpHttpServerOptions
{
    /// <summary>The host/interface to bind. Defaults to the loopback address.</summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>The TCP port to listen on.</summary>
    public int Port { get; init; } = 8931;

    /// <summary>
    /// An optional bearer token. When set, every request to the MCP endpoint must carry
    /// <c>Authorization: Bearer &lt;token&gt;</c>. Binding a non-loopback host without a token is
    /// refused.
    /// </summary>
    public string? Token { get; init; }

    /// <summary>
    /// How long a session may sit idle before the server tears it down (and its browser with it).
    /// Defaults to 30 minutes so abandoned remote sessions do not keep a browser alive indefinitely.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>The browser launch options each session uses when it first needs a page.</summary>
    public McpServerLaunchOptions LaunchOptions { get; init; } = new();
}
