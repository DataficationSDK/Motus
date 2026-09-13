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
    /// Registers the Motus tool classes on the builder: the ones every session gets, plus the
    /// optional groups <paramref name="options"/> asked for. Tools are listed explicitly (not by
    /// assembly scanning) so the schema is generated without runtime reflection and stays AOT-clean.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The always-on set is what an agent needs to see a page, act on it, and find out what
    /// happened. The optional groups are the ones a session either uses throughout or not at all,
    /// and every tool in the catalog is described to the client whether it is called or not, so
    /// leaving them out keeps the standing cost of connecting down. Attaching to a browser that is
    /// already running is listed on the same terms as it is allowed: the tool appears only when the
    /// server was started with the option or endpoint that lets it succeed.
    /// </para>
    /// <para>
    /// One filter is registered alongside the tools, so that a result from any tool says when a
    /// JavaScript dialog is waiting to be answered. It belongs here rather than in the tools
    /// because it is true of the session, not of any one call: while a dialog is up the browser
    /// answers nothing, so whatever the agent tried next is not going to work either.
    /// </para>
    /// </remarks>
    public static IMcpServerBuilder AddMotusTools(
        this IMcpServerBuilder mcpBuilder, McpServerLaunchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(mcpBuilder);

        var capabilities = options?.Capabilities;

        mcpBuilder.WithTools<CoreTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<InteractionTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<SessionTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<FrameTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<PageTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<NetworkTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<ConsoleTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<AccessibilityTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<PerformanceTools>(McpJsonUtilities.DefaultOptions);
        mcpBuilder.WithTools<CodegenTools>(McpJsonUtilities.DefaultOptions);

        if (ToolCapabilities.Includes(capabilities, ToolCapabilities.Coordinates))
            mcpBuilder.WithTools<CoordinateTools>(McpJsonUtilities.DefaultOptions);

        if (ToolCapabilities.Includes(capabilities, ToolCapabilities.Recording))
            mcpBuilder.WithTools<RecordingTools>(McpJsonUtilities.DefaultOptions);

        if (ToolCapabilities.Includes(capabilities, ToolCapabilities.Contexts))
            mcpBuilder.WithTools<ContextTools>(McpJsonUtilities.DefaultOptions);

        if (ToolCapabilities.Includes(capabilities, ToolCapabilities.Routing))
            mcpBuilder.WithTools<RoutingTools>(McpJsonUtilities.DefaultOptions);

        // Listing a tool that is going to refuse every call teaches an agent to try it; listing it
        // only when it can work says the same thing without the attempt. The policy check inside
        // the tool stays as the second line of defence.
        if (options?.AllowAttach == true || !string.IsNullOrEmpty(options?.Endpoint))
            mcpBuilder.WithTools<BrowserTools>(McpJsonUtilities.DefaultOptions);

        mcpBuilder.WithRequestFilters(filters => filters.AddCallToolFilter(
            next => async (context, cancellationToken) =>
            {
                var result = await next(context, cancellationToken).ConfigureAwait(false);
                return DialogNotice.Prefix(context.Services?.GetService<DialogService>(), result);
            }));

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
