using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tools for the isolated browser contexts a session can hold. Each context has its own
/// cookies and storage, so separate contexts model separate users, or a signed-in and a
/// signed-out state side by side. The tools that read or act on a page always target the
/// active context's active tab.
/// </summary>
/// <remarks>
/// Like the other tools, failures are returned as a result with
/// <see cref="CallToolResult.IsError"/> set and a message the model can act on, rather than
/// thrown. Switching context drops the refs from the previous snapshot, so the agent should
/// snapshot again before addressing elements.
/// </remarks>
[McpServerToolType]
public sealed class ContextTools
{
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
        if (ToolArguments.Missing("name", name) is { } missing)
            return missing;

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
        if (ToolArguments.Missing("name", name) is { } missing)
            return missing;

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
        if (ToolArguments.Missing("name", name) is { } missing)
            return missing;

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
