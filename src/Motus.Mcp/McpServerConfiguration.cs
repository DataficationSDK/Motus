using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Shared server configuration so the stdio host (<see cref="McpServerHost"/>) and the HTTP host
/// register the identical tool set and advertise the same server identity. Keeping it in one place
/// means the two transports cannot drift.
/// </summary>
public static class McpServerConfiguration
{
    private const string ServerName = "motus";

    /// <summary>
    /// Registers every Motus tool class on the builder. Tools are listed explicitly (not by
    /// assembly scanning) so the schema is generated without runtime reflection and stays AOT-clean.
    /// </summary>
    public static IMcpServerBuilder AddMotusTools(this IMcpServerBuilder mcpBuilder)
    {
        ArgumentNullException.ThrowIfNull(mcpBuilder);

        mcpBuilder.WithTools<CoreTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<InteractionTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<CoordinateTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<SessionTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<PageTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<NetworkTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<ConsoleTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<AccessibilityTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<PerformanceTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<RecordingTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<CodegenTools>(McpJsonUtilities.DefaultOptions);

        return mcpBuilder;
    }

    /// <summary>Sets the server name, version, and agent-facing instructions.</summary>
    public static void ConfigureServerOptions(McpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ServerInfo = new Implementation
        {
            Name = ServerName,
            Version = typeof(McpServerConfiguration).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        };
        options.ServerInstructions =
            "Motus drives a real browser for web automation and testing. Tool calls act on the active "
            + "browser context and tab unless directed otherwise.";
    }
}
