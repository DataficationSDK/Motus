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
        CancellationToken cancellationToken,
        SecurityPolicy? policy = null)
    {
        if (ToolArguments.Missing("url", url) is { } missing)
            return missing;

        if ((policy ?? SecurityPolicy.Default).RefuseUrl(url) is { } refusal)
            return ToolResultHelper.Error(refusal);

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            return await ActionRunner.RunAsync(pageService.Dialogs, cancellationToken, async _ =>
            {
                await page.GotoAsync(url, pageService.Navigation).ConfigureAwait(false);
                pageService.InvalidateSnapshot(page);
                return ToolResultHelper.Text($"Navigated to {url}");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Navigation failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "snapshot", Title = "Accessibility snapshot", Destructive = false, ReadOnly = true)]
    [Description("Returns an indented accessibility tree of the active page, or of the scoped frame when one is "
        + "selected. Each addressable element is tagged with a ref (e1, e2, ...) that click and type use to "
        + "address it. A page tree describes each iframe element but not its contents; frame_select looks inside.")]
    public static async Task<CallToolResult> SnapshotAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Root the snapshot at the subtree of this ref from the previous snapshot.")] string? root_ref = null,
        [Description("Limit how many levels deep the tree is rendered; 0 renders only the root.")] int? max_depth = null)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var frame = pageService.GetActiveFrame();

            // Reading the tree is a request to the renderer like any other, so it goes through the
            // runner too: a page stopped on a dialog cannot answer it either.
            return await ActionRunner.RunAsync(pageService.Dialogs, cancellationToken, async token =>
            {
                var text = await pageService.GetSnapshotService(page)
                    .TakeSnapshotAsync(frame, root_ref, max_depth, token)
                    .ConfigureAwait(false);

                // Said on every scoped snapshot rather than only on the frame_select that set the
                // scope, because the two are often several calls apart and a tree that silently
                // describes a different document than the agent expects is hard to notice.
                if (frame is not null)
                    text = $"Scoped to frame {frame.Url}\n\n{text}";

                return ToolResultHelper.Text(text);
            }).ConfigureAwait(false);
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
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Double-click instead of a single click.")] bool? @double = null)
    {
        if (ToolArguments.Missing("ref", @ref) is { } missing)
            return missing;

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var locator = pageService.GetSnapshotService(page).ResolveRef(@ref);

            return await ActionRunner.RunAsync(pageService.Dialogs, cancellationToken, async _ =>
            {
                if (@double == true)
                    await locator.DblClickAsync(pageService.ActionTimeout).ConfigureAwait(false);
                else
                    await locator.ClickAsync(pageService.ActionTimeout).ConfigureAwait(false);

                return ToolResultHelper.Text($"Clicked {@ref}");
            }).ConfigureAwait(false);
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
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Press Enter after entering the text.")] bool? submit = null,
        [Description("Type character by character instead of setting the value at once.")] bool? slowly = null)
    {
        if (ToolArguments.Missing("ref", @ref) is { } missingRef)
            return missingRef;
        if (ToolArguments.Unset("text", text) is { } missingText)
            return missingText;

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var locator = pageService.GetSnapshotService(page).ResolveRef(@ref);

            return await ActionRunner.RunAsync(pageService.Dialogs, cancellationToken, async _ =>
            {
                if (slowly == true)
                    await locator.TypeAsync(text).ConfigureAwait(false);
                else
                    await locator.FillAsync(text, pageService.ActionTimeout).ConfigureAwait(false);

                if (submit == true)
                    await locator.PressAsync("Enter").ConfigureAwait(false);

                return ToolResultHelper.Text($"Typed into {@ref}");
            }).ConfigureAwait(false);
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
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Capture the full scrollable page instead of just the viewport.")] bool? full_page = null)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            return await ActionRunner.RunAsync(pageService.Dialogs, cancellationToken, async _ =>
            {
                var bytes = await page.ScreenshotAsync(new ScreenshotOptions { FullPage = full_page ?? false })
                    .ConfigureAwait(false);
                return ToolResultHelper.Image(bytes);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Screenshot failed: {ex.Message}");
        }
    }

    private static CallToolResult NoSnapshot() => ToolResultHelper.NoSnapshot();

    private static CallToolResult Stale(StaleRefException ex) => ToolResultHelper.Stale(ex);
}
