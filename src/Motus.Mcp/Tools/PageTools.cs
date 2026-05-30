using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tools that operate on the active page as a whole: moving through its history,
/// answering a JavaScript dialog it raises, and evaluating an expression in it.
/// </summary>
/// <remarks>
/// Failures are returned as a result with <see cref="CallToolResult.IsError"/> set
/// and a message the model can act on, rather than thrown. History navigation drops
/// the refs from the previous snapshot, so the agent should snapshot again before
/// addressing elements afterwards.
/// </remarks>
[McpServerToolType]
public sealed class PageTools
{
    [McpServerTool(Name = "go_back", Title = "Go back", Destructive = false)]
    [Description("Navigates the active tab back one entry in its history. Reports when there was no entry to go to.")]
    public static async Task<CallToolResult> GoBackAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var response = await page.GoBackAsync().ConfigureAwait(false);
            pageService.InvalidateSnapshot(page);
            return ToolResultHelper.Text(response is null
                ? "No previous history entry; the page did not change."
                : $"Navigated back to {page.Url}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Go back failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "go_forward", Title = "Go forward", Destructive = false)]
    [Description("Navigates the active tab forward one entry in its history. Reports when there was no entry to go to.")]
    public static async Task<CallToolResult> GoForwardAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var response = await page.GoForwardAsync().ConfigureAwait(false);
            pageService.InvalidateSnapshot(page);
            return ToolResultHelper.Text(response is null
                ? "No next history entry; the page did not change."
                : $"Navigated forward to {page.Url}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Go forward failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "reload", Title = "Reload", Destructive = false)]
    [Description("Reloads the active tab and waits for it to finish loading.")]
    public static async Task<CallToolResult> ReloadAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            await page.ReloadAsync().ConfigureAwait(false);
            pageService.InvalidateSnapshot(page);
            return ToolResultHelper.Text($"Reloaded {page.Url}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Reload failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "handle_dialog", Title = "Handle a dialog", Destructive = true)]
    [Description("Accepts or dismisses the open JavaScript dialog (alert, confirm, or prompt). A dialog blocks the "
        + "page until it is handled. Returns an error when no dialog is open. For a prompt, text is the value to "
        + "enter when accepting.")]
    public static async Task<CallToolResult> HandleDialogAsync(
        [Description("True to accept the dialog, false to dismiss it.")] bool accept,
        [Description("Text to enter in a prompt dialog when accepting. Ignored for alert and confirm.")] string? text,
        DialogService dialogService,
        CancellationToken cancellationToken)
    {
        var dialog = dialogService.TakePendingDialog();
        if (dialog is null)
            return ToolResultHelper.Error("No dialog is open.");

        try
        {
            if (accept)
                await dialog.AcceptAsync(text).ConfigureAwait(false);
            else
                await dialog.DismissAsync().ConfigureAwait(false);

            return ToolResultHelper.Text(
                $"Dialog ({dialog.Type}) {(accept ? "accepted" : "dismissed")}: {dialog.Message}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Handling dialog failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "evaluate", Title = "Evaluate JavaScript", Destructive = true)]
    [Description("Evaluates a JavaScript expression and returns its result as structured JSON. With no ref it runs "
        + "in the page; with a ref it runs against that element, passed as the function's argument. Results that "
        + "cannot be serialized (undefined, functions, DOM nodes) come back as null.")]
    public static async Task<CallToolResult> EvaluateAsync(
        [Description("The JavaScript expression to evaluate.")] string expression,
        [Description("An element ref from the latest snapshot to evaluate against. Omit to evaluate in the page.")] string? @ref,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(@ref))
            {
                var pageResult = await page.EvaluateAsync<JsonElement>(expression).ConfigureAwait(false);
                return ToolResultHelper.Structured(pageResult);
            }

            var locator = pageService.GetSnapshotService(page).ResolveRef(@ref);
            var elementResult = await locator.EvaluateWithElementAsync<JsonElement>(expression).ConfigureAwait(false);
            return ToolResultHelper.Structured(elementResult);
        }
        catch (SnapshotNotTakenException)
        {
            return ToolResultHelper.NoSnapshot();
        }
        catch (StaleRefException ex)
        {
            return ToolResultHelper.Stale(ex);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Evaluate failed: {ex.Message}");
        }
    }
}
