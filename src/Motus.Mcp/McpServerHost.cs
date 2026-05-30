using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Hosts the Motus MCP server. The server speaks JSON-RPC over a transport,
/// holds the browser session for its lifetime, and tears the browser down when
/// the host stops (for example when the client disconnects).
/// </summary>
public static class McpServerHost
{
    private const string ServerName = "motus";

    /// <summary>
    /// Runs the server over the standard input/output streams until the client
    /// disconnects or the token is cancelled. This is the entry point a CLI host
    /// invokes.
    /// </summary>
    public static Task RunAsync(McpServerLaunchOptions options, CancellationToken cancellationToken = default)
        => RunAsync(options, builder => builder.WithStdioServerTransport(), cancellationToken);

    /// <summary>
    /// Runs the server with a caller-supplied transport. Exists so the standard
    /// stdio path and in-process stream transports share the exact same server
    /// wiring.
    /// </summary>
    internal static async Task RunAsync(
        McpServerLaunchOptions options,
        Action<IMcpServerBuilder> configureTransport,
        CancellationToken cancellationToken)
    {
        // Start from an empty host: no configuration scanning, no environment
        // probing. An stdio server needs none of it, and the leaner builder keeps
        // the trim/AOT surface small.
        var builder = Host.CreateEmptyApplicationBuilder(settings: null);

        // stdout is the JSON-RPC channel. Every log line must go to stderr so it
        // never corrupts the protocol stream.
        builder.Logging.AddConsole(consoleOptions =>
        {
            consoleOptions.LogToStandardErrorThreshold = LogLevel.Trace;
        });

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<BrowserSessionManager>();
        builder.Services.AddSingleton<DialogService>();
        builder.Services.AddSingleton<ConsoleService>();
        builder.Services.AddSingleton<NetworkService>();
        builder.Services.AddSingleton(sp => new ActivePageService(
            sp.GetRequiredService<BrowserSessionManager>(),
            sp.GetRequiredService<DialogService>(),
            sp.GetRequiredService<ConsoleService>(),
            sp.GetRequiredService<NetworkService>()));

        var mcpBuilder = builder.Services.AddMcpServer(ConfigureServerOptions);
        configureTransport(mcpBuilder);

        // Register the tools explicitly (not by assembly scanning) so the schema is
        // generated without runtime reflection and stays AOT-clean.
        mcpBuilder.WithTools<CoreTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<InteractionTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<SessionTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<PageTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<NetworkTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<ConsoleTools>(McpJsonUtilities.DefaultOptions);

        using var host = builder.Build();

        // RunAsync disposes the host (asynchronously, since it implements
        // IAsyncDisposable) on shutdown, which disposes the singleton session
        // manager and tears the browser down. No explicit disposal is needed.
        await host.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static void ConfigureServerOptions(McpServerOptions options)
    {
        options.ServerInfo = new Implementation
        {
            Name = ServerName,
            Version = typeof(McpServerHost).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        };
        options.ServerInstructions =
            "Motus drives a real browser for web automation and testing. Tool calls act on the active "
            + "browser context and tab unless directed otherwise.";
    }
}
