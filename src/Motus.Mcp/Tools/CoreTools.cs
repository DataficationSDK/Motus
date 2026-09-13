using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// The core set of tools an agent uses to drive a page: read it, navigate it, act
/// on it, and capture it. Each tool acts on the active context's active page and
/// addresses elements either by the refs a <c>snapshot</c> assigns or by a selector.
/// </summary>
/// <remarks>
/// Tools report failures as a result with <see cref="CallToolResult.IsError"/> set
/// and a message the model can act on, rather than by throwing: a thrown exception
/// becomes a protocol error and loses the guidance. Acting on a ref before any
/// snapshot, or on a ref from a stale snapshot, returns a message telling the agent
/// to snapshot again; a selector carries its own description of the element, so it
/// never needs one.
/// </remarks>
[McpServerToolType]
public sealed class CoreTools
{
    [McpServerTool(Name = "navigate", Title = "Navigate to URL", Destructive = true)]
    [Description("Navigates the active page to a URL, waits for it to load, and reports the page it ended up on "
        + "with its title. Refs from any earlier snapshot stop meaning anything here, so take a snapshot before "
        + "addressing elements, or pass snapshot: true to get one with the result.")]
    public static async Task<CallToolResult> NavigateAsync(
        [Description("The URL to navigate to.")] string url,
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Append a snapshot of the page after the action.")] bool? snapshot = null,
        SecurityPolicy? policy = null)
    {
        if (ToolArguments.Missing("url", url) is { } missing)
            return missing;

        if ((policy ?? SecurityPolicy.Default).RefuseUrl(url) is { } refusal)
            return ToolResultHelper.Error(refusal);

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            return await ActionRunner.RunAsync(pageService, page, cancellationToken, async _ =>
            {
                await page.GotoAsync(url, pageService.Navigation).ConfigureAwait(false);
                pageService.InvalidateSnapshot(page);

                // The title is what tells the agent whether the address it asked for was the page it
                // wanted, and a redirect or a login wall is exactly where the two come apart.
                var title = await PageDescription.TryTitleAsync(page).ConfigureAwait(false);
                return ToolResultHelper.Text($"Navigated to {PageDescription.Of(url, title)}");
            }, snapshot == true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Navigation failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "snapshot", Title = "Accessibility snapshot", Destructive = false, ReadOnly = true)]
    [Description("Returns a compact indented accessibility tree of the active page, or of the scoped frame when "
        + "one is selected. Interactive, named, and focusable elements and iframes carry a ref (e1, e2, ...) "
        + "that click and type use to address them, and those tools take a selector just as readily; other "
        + "nodes are printed for context without a ref. Text is "
        + "printed inline on its element's line, headings show [level=N], links show [url=...], and form "
        + "controls show [value=\"...\"] and state flags. A page tree contains the frames the page hosts, "
        + "printed inside the iframe element that holds each and marked [frame=N]; refs inside frame 1 read "
        + "f1e2 and act on that frame without selecting it first.")]
    public static async Task<CallToolResult> SnapshotAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Root the snapshot at the subtree of this ref from the previous snapshot. A ref inside a "
            + "frame (f1e2) roots it in that frame.")] string? root_ref = null,
        [Description("Limit how many levels deep the tree is rendered; 0 renders only the root. A frame's "
            + "contents count as levels of the tree like anything else.")] int? max_depth = null,
        [Description("Limit how many frames are printed inside the page; 10 by default. Frames past the limit "
            + "are named by frame_list and read with frame_select.")] int? max_frames = null)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var frame = pageService.GetActiveFrame();

            // Reading the tree is a request to the renderer like any other, so it goes through the
            // runner too: a page stopped on a dialog cannot answer it either.
            return await ActionRunner.RunAsync(pageService.Dialogs, cancellationToken, async token =>
            {
                var snapshots = pageService.GetSnapshotService(page);
                var text = await snapshots
                    .TakeSnapshotAsync(frame, root_ref, max_depth, max_frames, token)
                    .ConfigureAwait(false);

                // Said on every scoped snapshot rather than only on the frame_select that set the
                // scope, because the two are often several calls apart and a tree that silently
                // describes a different document than the agent expects is hard to notice. The
                // scope is read back rather than assumed, since rooting at a ref inside a frame
                // scopes the snapshot to that frame whether or not one was selected.
                if (snapshots.Scope is { } scoped)
                    text = $"Scoped to frame {scoped.Url}\n\n{text}";

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
    [Description("Clicks the element addressed by a ref from the latest snapshot or by a selector. "
        + "A right-click, a middle-click, and a click with modifier keys held all go through the "
        + "same actionability checks as a plain one. The result names anything the click changed: "
        + "where the page went, a tab it opened, errors it logged, a dialog it raised.")]
    public static async Task<CallToolResult> ClickAsync(
        [Description("The element to click. " + ToolDescriptions.Target)] string @ref,
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Double-click instead of a single click.")] bool? @double = null,
        [Description("The mouse button: left (default), right, or middle.")] string? button = null,
        [Description("Modifier keys held during the click: Alt, Control, Meta, Shift.")] string[]? modifiers = null,
        [Description("Append a snapshot of the page after the action.")] bool? snapshot = null)
    {
        if (ToolArguments.Missing("ref", @ref) is { } missing)
            return missing;
        if (ToolArguments.Button(button, out var parsedButton) is { } unknownButton)
            return unknownButton;
        if (ToolArguments.Modifiers(modifiers, out var parsedModifiers) is { } unknownModifier)
            return unknownModifier;

        // A double-click dispatches its own pair of press and release events and takes no button or
        // modifiers, so a caller asking for both is told rather than quietly given a plain
        // double-click. click_xy double-clicks with a button and modifiers when that is wanted.
        if (@double == true && (parsedButton is not MouseButton.Left || parsedModifiers is not KeyModifier.None))
            return ToolResultHelper.Error(
                "A double-click uses the left button with no modifiers. Drop double, or use click_xy "
                + "with the element's coordinates for a modified double-click.");

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var locator = pageService.GetSnapshotService(page).ResolveRef(@ref, pageService.GetActiveFrame());

            return await ActionRunner.RunAsync(pageService, page, cancellationToken, async _ =>
            {
                if (@double == true)
                    await locator.DblClickAsync(pageService.ActionTimeout).ConfigureAwait(false);
                else if (parsedButton is MouseButton.Left && parsedModifiers is KeyModifier.None)
                    await locator.ClickAsync(pageService.ActionTimeout).ConfigureAwait(false);
                else
                    await locator.ClickAsync(
                        new MouseButtonOptions(Button: parsedButton, Modifiers: parsedModifiers),
                        pageService.ActionTimeout).ConfigureAwait(false);

                return ToolResultHelper.Text($"Clicked {@ref}");
            }, snapshot == true).ConfigureAwait(false);
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
    [Description("Types text into the element addressed by a ref from the latest snapshot or by a selector.")]
    public static async Task<CallToolResult> TypeAsync(
        [Description("The element to type into. " + ToolDescriptions.Target)] string @ref,
        [Description("The text to enter.")] string text,
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Press Enter after entering the text.")] bool? submit = null,
        [Description("Type character by character instead of setting the value at once.")] bool? slowly = null,
        [Description("Append a snapshot of the page after the action.")] bool? snapshot = null)
    {
        if (ToolArguments.Missing("ref", @ref) is { } missingRef)
            return missingRef;
        if (ToolArguments.Unset("text", text) is { } missingText)
            return missingText;

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var locator = pageService.GetSnapshotService(page).ResolveRef(@ref, pageService.GetActiveFrame());

            return await ActionRunner.RunAsync(pageService, page, cancellationToken, async _ =>
            {
                if (slowly == true)
                    await locator.TypeAsync(text, new KeyboardTypeOptions(Timeout: pageService.ActionTimeout))
                        .ConfigureAwait(false);
                else
                    await locator.FillAsync(text, pageService.ActionTimeout).ConfigureAwait(false);

                if (submit == true)
                    await locator.PressAsync("Enter", new KeyboardPressOptions(Timeout: pageService.ActionTimeout))
                        .ConfigureAwait(false);

                return ToolResultHelper.Text($"Typed into {@ref}");
            }, snapshot == true).ConfigureAwait(false);
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
