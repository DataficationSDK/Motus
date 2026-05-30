using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// The core set of tools an agent uses to drive a page: read it, navigate it, act
/// on it, and capture it. Each tool acts on the active context's active page and
/// addresses elements by the refs a <c>snapshot</c> assigns.
/// </summary>
/// <remarks>
/// Tools report failures as a result with <see cref="CallToolResult.IsError"/> set
/// and a message the model can act on, rather than by throwing: a thrown exception
/// becomes a protocol error and loses the guidance. Acting on a ref before any
/// snapshot, or on a ref from a stale snapshot, returns a message telling the agent
/// to snapshot again.
/// </remarks>
[McpServerToolType]
public sealed class CoreTools
{
    [McpServerTool(Name = "navigate", Title = "Navigate to URL", Destructive = true)]
    [Description("Navigates the active page to a URL and waits for it to load.")]
    public static async Task<CallToolResult> NavigateAsync(
        [Description("The URL to navigate to.")] string url,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            await page.GotoAsync(url).ConfigureAwait(false);
            pageService.InvalidateSnapshot(page);
            return ToolResultHelper.Text($"Navigated to {url}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Navigation failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "snapshot", Title = "Accessibility snapshot", Destructive = false, ReadOnly = true)]
    [Description("Returns an indented accessibility tree of the active page. Each addressable element is "
        + "tagged with a ref (e1, e2, ...) that click and type use to address it.")]
    public static async Task<CallToolResult> SnapshotAsync(
        [Description("Root the snapshot at the subtree of this ref from the previous snapshot.")] string? root_ref,
        [Description("Limit how many levels deep the tree is rendered; 0 renders only the root.")] int? max_depth,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var text = await pageService.GetSnapshotService(page)
                .TakeSnapshotAsync(root_ref, max_depth, cancellationToken)
                .ConfigureAwait(false);
            return ToolResultHelper.Text(text);
        }
        catch (SnapshotNotTakenException)
        {
            return ToolResultHelper.Error("No snapshot has been taken. Call snapshot without root_ref first.");
        }
        catch (StaleRefException ex)
        {
            return ToolResultHelper.Error(
                $"Ref '{ex.RefId}' is not in the latest snapshot. Take a full snapshot to refresh refs.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Snapshot failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "click", Title = "Click element", Destructive = true)]
    [Description("Clicks the element addressed by a ref from the latest snapshot.")]
    public static async Task<CallToolResult> ClickAsync(
        [Description("The element ref from the latest snapshot, e.g. e5.")] string @ref,
        [Description("Double-click instead of a single click.")] bool? @double,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var locator = pageService.GetSnapshotService(page).ResolveRef(@ref);

            if (@double == true)
                await locator.DblClickAsync().ConfigureAwait(false);
            else
                await locator.ClickAsync().ConfigureAwait(false);

            return ToolResultHelper.Text($"Clicked {@ref}");
        }
        catch (SnapshotNotTakenException)
        {
            return NoSnapshot();
        }
        catch (StaleRefException ex)
        {
            return Stale(ex);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Click failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "type", Title = "Type into element", Destructive = true)]
    [Description("Types text into the element addressed by a ref from the latest snapshot.")]
    public static async Task<CallToolResult> TypeAsync(
        [Description("The element ref from the latest snapshot, e.g. e3.")] string @ref,
        [Description("The text to enter.")] string text,
        [Description("Press Enter after entering the text.")] bool? submit,
        [Description("Type character by character instead of setting the value at once.")] bool? slowly,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var locator = pageService.GetSnapshotService(page).ResolveRef(@ref);

            if (slowly == true)
                await locator.TypeAsync(text).ConfigureAwait(false);
            else
                await locator.FillAsync(text).ConfigureAwait(false);

            if (submit == true)
                await locator.PressAsync("Enter").ConfigureAwait(false);

            return ToolResultHelper.Text($"Typed into {@ref}");
        }
        catch (SnapshotNotTakenException)
        {
            return NoSnapshot();
        }
        catch (StaleRefException ex)
        {
            return Stale(ex);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Type failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "screenshot", Title = "Screenshot", Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Captures a PNG screenshot of the active page and returns it as an image.")]
    public static async Task<CallToolResult> ScreenshotAsync(
        [Description("Capture the full scrollable page instead of just the viewport.")] bool? full_page,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var bytes = await page.ScreenshotAsync(new ScreenshotOptions { FullPage = full_page ?? false })
                .ConfigureAwait(false);
            return ToolResultHelper.Image(bytes);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Screenshot failed: {ex.Message}");
        }
    }

    private static CallToolResult NoSnapshot()
        => ToolResultHelper.Error("No snapshot has been taken. Call snapshot first, then retry with a ref from it.");

    private static CallToolResult Stale(StaleRefException ex)
        => ToolResultHelper.Error(
            $"Ref '{ex.RefId}' is not in the latest snapshot. Call snapshot to refresh refs, then retry.");
}
