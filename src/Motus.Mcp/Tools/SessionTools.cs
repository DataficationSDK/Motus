using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tools for the browser around the page: the tabs of the active context and the
/// isolated contexts themselves. Each context has its own cookies and storage, so
/// separate contexts model separate users or logged-in and logged-out states. The
/// tools that read or act on a page always target the active context's active tab.
/// </summary>
/// <remarks>
/// Like the other tools, failures are returned as a result with
/// <see cref="CallToolResult.IsError"/> set and a message the model can act on,
/// rather than thrown. Switching tab or context drops the refs from the previous
/// snapshot, so the agent should snapshot again before addressing elements.
/// </remarks>
[McpServerToolType]
public sealed class SessionTools
{
    [McpServerTool(Name = "tab_list", Title = "List tabs", Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Lists the open tabs of the active context, each with its zero-based index, URL, and title.")]
    public static async Task<CallToolResult> TabListAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var pages = await pageService.ListTabsAsync(cancellationToken).ConfigureAwait(false);
            if (pages.Count == 0)
                return ToolResultHelper.Text("No tabs are open.");

            var builder = new StringBuilder();
            for (var i = 0; i < pages.Count; i++)
            {
                var title = await pages[i].TitleAsync().ConfigureAwait(false);
                builder.Append('[').Append(i).Append("] ").Append(pages[i].Url);
                if (!string.IsNullOrEmpty(title))
                    builder.Append(" | ").Append(title);
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
        [Description("URL to open the new tab at. Omit to open a blank tab.")] string? url,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.OpenNewTabAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(url))
                await page.GotoAsync(url).ConfigureAwait(false);

            pageService.InvalidateSnapshot(page);
            return ToolResultHelper.Text($"Opened tab at {(string.IsNullOrEmpty(url) ? "about:blank" : url)}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Opening tab failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "tab_select", Title = "Select a tab", Destructive = false)]
    [Description("Makes the tab at the given zero-based index active. Indices come from tab_list; call it first "
        + "if you are unsure of the current order.")]
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
    [Description("Closes the tab at the given zero-based index, or the active tab when no index is given. The "
        + "next available tab becomes active.")]
    public static async Task<CallToolResult> TabCloseAsync(
        [Description("Zero-based index of the tab to close. Omit to close the active tab.")] int? index,
        ActivePageService pageService,
        CancellationToken cancellationToken)
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

    [McpServerTool(Name = "context_list", Title = "List contexts", Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Lists the open browser contexts. The active context is marked with an asterisk.")]
    public static CallToolResult ContextList(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        var active = pageService.GetActiveContextName();
        var names = pageService.GetContextNames();
        if (names.Count == 0)
            return ToolResultHelper.Text($"No contexts are open yet; '{active}' becomes active on first use.");

        var builder = new StringBuilder();
        foreach (var name in names)
            builder.Append(name == active ? "* " : "  ").AppendLine(name);

        return ToolResultHelper.Text(builder.ToString().TrimEnd());
    }

    [McpServerTool(Name = "context_create", Title = "Create a context", Destructive = true)]
    [Description("Creates a new isolated context with its own cookies and storage and makes it active. Fails if a "
        + "context with that name already exists.")]
    public static async Task<CallToolResult> ContextCreateAsync(
        [Description("Name for the new context.")] string name,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            await pageService.CreateContextAsync(name, cancellationToken).ConfigureAwait(false);
            return ToolResultHelper.Text($"Created context '{name}'.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error(ex.Message);
        }
    }

    [McpServerTool(Name = "context_select", Title = "Select a context", Destructive = false)]
    [Description("Makes an existing context active. The tabs and page tools that follow act on its tabs.")]
    public static CallToolResult ContextSelect(
        [Description("Name of the context to activate.")] string name,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            pageService.SelectContext(name);
            return ToolResultHelper.Text($"Switched to context '{name}'.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error(ex.Message);
        }
    }

    [McpServerTool(Name = "context_close", Title = "Close a context", Destructive = true)]
    [Description("Closes the named context and all its tabs. If the active context is closed, the default context "
        + "becomes active.")]
    public static async Task<CallToolResult> ContextCloseAsync(
        [Description("Name of the context to close.")] string name,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            await pageService.CloseContextAsync(name, cancellationToken).ConfigureAwait(false);
            return ToolResultHelper.Text($"Closed context '{name}'.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error(ex.Message);
        }
    }
}
