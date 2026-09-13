using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tools for the tabs a session has open, and for saying which browser the session is driving. The
/// tools that read or act on a page always target the active tab, so these are how the agent moves
/// between the pages a session has open.
/// </summary>
/// <remarks>
/// Like the other tools, failures are returned as a result with
/// <see cref="CallToolResult.IsError"/> set and a message the model can act on,
/// rather than thrown. Switching tab drops the refs from the previous snapshot, so the
/// agent should snapshot again before addressing elements.
/// </remarks>
[McpServerToolType]
public sealed class SessionTools
{
    [McpServerTool(Name = "tab_list", Title = "List tabs", Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Lists every open tab this session has, across all its contexts, each with its zero-based index, "
        + "URL, and title. The context a tab belongs to is named when more than one is open. The index is the one "
        + "tab_select and tab_close take, and selecting a tab in another context switches to that context.")]
    public static async Task<CallToolResult> TabListAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var tabs = await pageService.ListTabsAsync(cancellationToken).ConfigureAwait(false);
            if (tabs.Count == 0)
                return ToolResultHelper.Text("No tabs are open.");

            // Naming the context on every row of a session that only ever has one would be a column
            // of the same word, so it is printed only once there is a choice to make.
            var named = pageService.GetContextNames().Count > 1;

            var builder = new StringBuilder();
            for (var i = 0; i < tabs.Count; i++)
            {
                // A tab that will not say what it is called is listed by its address. A tab still
                // opening, or one whose renderer is busy, must not take the whole listing down with
                // it: the listing is how an agent finds the tab it wants in the first place.
                var title = await PageDescription.TryTitleAsync(tabs[i].Page).ConfigureAwait(false);
                builder.Append('[').Append(i).Append("] ").Append(tabs[i].Page.Url);
                if (!string.IsNullOrEmpty(title))
                    builder.Append(" | ").Append(title);
                if (named)
                    builder.Append(" | context: ").Append(tabs[i].ContextName);
                builder.AppendLine();
            }

            return ToolResultHelper.Text(builder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Listing tabs failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "tab_open", Title = "Open a tab", Destructive = true)]
    [Description("Opens a new tab in the active context and makes it active. Navigates it to the URL when one is "
        + "given. Take a snapshot before addressing elements in the new tab.")]
    public static async Task<CallToolResult> TabOpenAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("URL to open the new tab at. Omit to open a blank tab.")] string? url = null,
        SecurityPolicy? policy = null)
    {
        if ((policy ?? SecurityPolicy.Default).RefuseUrl(url) is { } refusal)
            return ToolResultHelper.Error(refusal);

        try
        {
            var page = await pageService.OpenNewTabAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(url))
                await page.GotoAsync(url, pageService.Navigation).ConfigureAwait(false);

            pageService.InvalidateSnapshot(page);
            return ToolResultHelper.Text($"Opened tab at {(string.IsNullOrEmpty(url) ? "about:blank" : url)}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Opening tab failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "tab_select", Title = "Select a tab", Destructive = false)]
    [Description("Makes the tab at the given zero-based index active. A tab in another context switches the "
        + "session to that context as well. Indices come from tab_list; call it first if you are unsure of the "
        + "current order.")]
    public static async Task<CallToolResult> TabSelectAsync(
        [Description("Zero-based index of the tab to activate.")] int index,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.SelectTabAsync(index, cancellationToken).ConfigureAwait(false);
            pageService.InvalidateSnapshot(page);
            return ToolResultHelper.Text($"Selected tab {index}: {page.Url}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error(ex.Message);
        }
    }

    [McpServerTool(Name = "tab_close", Title = "Close a tab", Destructive = true)]
    [Description("Closes the tab at the given zero-based index, in whichever context it is open, or the active "
        + "tab when no index is given. The next available tab becomes active.")]
    public static async Task<CallToolResult> TabCloseAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Zero-based index of the tab to close. Omit to close the active tab.")] int? index = null)
    {
        try
        {
            var closed = await pageService.CloseTabAsync(index, cancellationToken).ConfigureAwait(false);
            return ToolResultHelper.Text($"Closed tab {closed}.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error(ex.Message);
        }
    }

    [McpServerTool(Name = "browser_status", Title = "Browser status", Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Reports which browser the session is driving: whether it was started here or attached to, its "
        + "endpoint when attached, and how many contexts and tabs are open.")]
    public static async Task<CallToolResult> BrowserStatusAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!pageService.IsBrowserLaunched)
            {
                return ToolResultHelper.Text(pageService.Endpoint is { } configured
                    ? $"No browser yet. The first tool call that needs one will attach to {configured}."
                    : "No browser yet. The first tool call that needs one will start it.");
            }

            var tabs = await pageService.ListTabsAsync(cancellationToken).ConfigureAwait(false);
            var contexts = pageService.GetContextNames();

            var builder = new StringBuilder();
            builder.AppendLine(pageService.IsAttached
                ? $"Attached to a running browser at {pageService.Endpoint}; it will keep running after this session."
                : "Driving a browser started by this server; it will be closed when this session ends.");
            builder.Append(contexts.Count).Append(contexts.Count == 1 ? " context" : " contexts")
                .Append(" (active: ").Append(pageService.GetActiveContextName()).Append("), ")
                .Append(tabs.Count).Append(tabs.Count == 1 ? " tab" : " tabs").Append(" open.");

            return ToolResultHelper.Text(builder.ToString());
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Reading browser status failed: {ex.Message}");
        }
    }
}
