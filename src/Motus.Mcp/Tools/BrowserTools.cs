using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tools for connecting to a browser that is already running.
/// </summary>
/// <remarks>
/// By default the server starts a browser of its own and ends it on shutdown. Attaching points the
/// session at one that was started by somebody else, which is how an application already on screen,
/// a signed-in profile, or a warm browser kept between runs becomes drivable. That browser is never
/// ended by this session, but everything else about it is reachable, so the tools that close tabs
/// and contexts close somebody's working state rather than scratch state.
/// </remarks>
[McpServerToolType]
public sealed class BrowserTools
{
    [McpServerTool(Name = "browser_attach", Title = "Attach to a running browser", Destructive = true)]
    [Description("Connects to a browser that is already running and drives what is open in it, instead of the "
        + "browser this server started. Give the debugging endpoint the browser was started with, for example "
        + "http://127.0.0.1:9222, or its CDP WebSocket URL. The browser this server started, if any, is closed. "
        + "Snapshot refs, route rules, and captured console output do not survive the switch, so take a fresh "
        + "snapshot afterwards.")]
    public static async Task<CallToolResult> BrowserAttachAsync(
        [Description("The browser's debugging endpoint, e.g. http://127.0.0.1:9222, or its CDP WebSocket URL.")]
        string endpoint,
        ActivePageService pageService,
        CancellationToken cancellationToken,
        SecurityPolicy? policy = null)
    {
        if (ToolArguments.Missing("endpoint", endpoint) is { } missing)
            return missing;

        if ((policy ?? SecurityPolicy.Default).RefuseAttach() is { } refusal)
            return ToolResultHelper.Error(refusal);

        try
        {
            await pageService.AttachAsync(endpoint, cancellationToken).ConfigureAwait(false);

            var tabs = await pageService.ListTabsAsync(cancellationToken).ConfigureAwait(false);
            var builder = new StringBuilder();
            builder.Append("Attached to the browser at ").Append(endpoint).AppendLine(".");
            builder.Append("It has ").Append(tabs.Count).Append(tabs.Count == 1 ? " tab" : " tabs").Append(" open");
            builder.AppendLine(tabs.Count > 0 ? $", starting with {tabs[0].Page.Url}." : ".");
            builder.Append("This browser was not started here and will keep running when this session ends. ")
                .Append("Its tabs and contexts belong to whoever was using it, so tab_close and context_close ")
                .Append("discard their work rather than scratch state.");

            return ToolResultHelper.Text(builder.ToString());
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error(
                $"Attaching to {endpoint} failed: {ex.Message}. Check that the browser was started with a "
                + "remote debugging port and that the endpoint is reachable.");
        }
    }
}
