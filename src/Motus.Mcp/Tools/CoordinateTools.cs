using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Interactions at raw viewport coordinates: clicking, hovering, moving, wheel
/// scrolling, dragging, and resizing the viewport. These are the escape hatch for
/// surfaces the accessibility tree cannot describe, such as canvas or WebGL
/// rendering: take a screenshot, identify the control visually, and act on its
/// position directly. Coordinates are CSS pixels in the viewport coordinate
/// space, the same space screenshots and getBoundingClientRect() report.
/// </summary>
/// <remarks>
/// All input is dispatched as trusted browser-level events, and the browser's
/// native hit test routes each event to whatever element is rendered at the
/// point. There is no element resolution step and no actionability check, so
/// these tools work on targets that have no accessible node at all.
/// </remarks>
[McpServerToolType]
public sealed class CoordinateTools
{
    [McpServerTool(Name = "click_xy", Title = "Click at coordinates", Destructive = true)]
    [Description("Clicks at a viewport coordinate with trusted browser input. The browser's native hit test "
        + "decides the target, so this works on canvas and custom-rendered surfaces that have no refs. "
        + "Read positions from a screenshot or getBoundingClientRect(); both use this coordinate space.")]
    public static async Task<CallToolResult> ClickXyAsync(
        [Description("The x coordinate in CSS pixels from the left edge of the viewport.")] int x,
        [Description("The y coordinate in CSS pixels from the top edge of the viewport.")] int y,
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("Double-click instead of a single click.")] bool? @double = null,
        [Description("The mouse button: left (default), right, or middle.")] string? button = null,
        [Description("Modifier keys held during the click: Alt, Control, Meta, Shift.")] string[]? modifiers = null)
    {
        try
        {
            if (ToolArguments.Button(button, out var parsedButton) is { } unknownButton)
                return unknownButton;
            if (ToolArguments.Modifiers(modifiers, out var parsedModifiers) is { } unknownModifier)
                return unknownModifier;

            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var options = new MouseButtonOptions(Button: parsedButton, Modifiers: parsedModifiers);

            return await ActionRunner.RunAsync(pageService, page, cancellationToken, async _ =>
            {
                if (@double == true)
                    await page.Mouse.DblClickAsync(x, y, options).ConfigureAwait(false);
                else
                    await page.Mouse.ClickAsync(x, y, options).ConfigureAwait(false);

                return ToolResultHelper.Text($"{(@double == true ? "Double-clicked" : "Clicked")} at ({x}, {y})");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Click at ({x}, {y}) failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "hover_xy", Title = "Hover at coordinates", Destructive = true)]
    [Description("Moves the pointer to a viewport coordinate, triggering hover effects at that point. "
        + "Works on surfaces without refs, such as canvas rendering.")]
    public static Task<CallToolResult> HoverXyAsync(
        [Description("The x coordinate in CSS pixels from the left edge of the viewport.")] int x,
        [Description("The y coordinate in CSS pixels from the top edge of the viewport.")] int y,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => MoveCoreAsync(pageService, x, y, $"Hovering at ({x}, {y})", cancellationToken);

    [McpServerTool(Name = "move_xy", Title = "Move pointer to coordinates", Destructive = true)]
    [Description("Moves the pointer to a viewport coordinate without pressing a button. "
        + "Useful for positioning before a wheel scroll or observing pointer-tracking UI.")]
    public static Task<CallToolResult> MoveXyAsync(
        [Description("The x coordinate in CSS pixels from the left edge of the viewport.")] int x,
        [Description("The y coordinate in CSS pixels from the top edge of the viewport.")] int y,
        ActivePageService pageService,
        CancellationToken cancellationToken)
        => MoveCoreAsync(pageService, x, y, $"Moved pointer to ({x}, {y})", cancellationToken);

    [McpServerTool(Name = "scroll_xy", Title = "Wheel scroll at coordinates", Destructive = true)]
    [Description("Dispatches a trusted mouse wheel event at a viewport coordinate, scrolling whatever "
        + "scrollable region is under that point. Positive delta_y scrolls down, positive delta_x scrolls "
        + "right. Use this to bring canvas-painted content into view when scroll_into_view has no ref to target.")]
    public static async Task<CallToolResult> ScrollXyAsync(
        [Description("The x coordinate in CSS pixels from the left edge of the viewport.")] int x,
        [Description("The y coordinate in CSS pixels from the top edge of the viewport.")] int y,
        [Description("Horizontal scroll amount in CSS pixels; positive scrolls right.")] int delta_x,
        [Description("Vertical scroll amount in CSS pixels; positive scrolls down.")] int delta_y,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            return await ActionRunner.RunAsync(pageService, page, cancellationToken, async _ =>
            {
                // The wheel event is dispatched at the current pointer position, so
                // position the pointer first.
                await page.Mouse.MoveAsync(x, y).ConfigureAwait(false);
                await page.Mouse.WheelAsync(delta_x, delta_y).ConfigureAwait(false);
                return ToolResultHelper.Text($"Scrolled ({delta_x}, {delta_y}) at ({x}, {y})");
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Scroll at ({x}, {y}) failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "resize", Title = "Resize viewport", Destructive = false, Idempotent = true)]
    [Description("Resizes the page viewport. Use this when controls sit at or beyond the viewport edge; "
        + "subsequent screenshots and coordinates use the new size.")]
    public static async Task<CallToolResult> ResizeAsync(
        [Description("The new viewport width in CSS pixels.")] int width,
        [Description("The new viewport height in CSS pixels.")] int height,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        if (width <= 0 || height <= 0)
            return ToolResultHelper.Error("Width and height must be positive.");

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            await page.SetViewportSizeAsync(new ViewportSize(width, height)).ConfigureAwait(false);
            return ToolResultHelper.Text($"Viewport resized to {width}x{height}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Resize to {width}x{height} failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "drag", Title = "Drag and drop", Destructive = true)]
    [Description("Drags from one point to another with trusted input: press, intermediate moves, release. "
        + "Address the endpoints either by elements (start_ref/end_ref, each a ref or a selector, each scrolled into "
        + "view before the pointer goes to it) or by viewport coordinates (start_x/start_y/end_x/end_y); "
        + "coordinates work on canvas drop-zones that have no refs. "
        + "Intermediate moves are always emitted because drag libraries commonly require observed movement.")]
    public static async Task<CallToolResult> DragAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken,
        [Description("The element to drag. " + ToolDescriptions.Target)] string? start_ref = null,
        [Description("The drop target. " + ToolDescriptions.Target)] string? end_ref = null,
        [Description("The x coordinate to drag from, in CSS pixels.")] int? start_x = null,
        [Description("The y coordinate to drag from, in CSS pixels.")] int? start_y = null,
        [Description("The x coordinate to drop at, in CSS pixels.")] int? end_x = null,
        [Description("The y coordinate to drop at, in CSS pixels.")] int? end_y = null,
        [Description("Number of intermediate pointer moves between press and release. Default 10.")] int? steps = null,
        [Description("Milliseconds to hold the button down before moving, for libraries that threshold drag start.")] int? hold_ms = null)
    {
        var hasRefs = start_ref is not null || end_ref is not null;
        var hasCoords = start_x is not null || start_y is not null || end_x is not null || end_y is not null;
        if (hasRefs == hasCoords)
            return ToolResultHelper.Error(
                "Provide either start_ref and end_ref, or all of start_x, start_y, end_x, end_y, but not both.");
        if (hasRefs && (start_ref is null || end_ref is null))
            return ToolResultHelper.Error("Both start_ref and end_ref are required when dragging by ref.");
        if (hasCoords && (start_x is null || start_y is null || end_x is null || end_y is null))
            return ToolResultHelper.Error(
                "All of start_x, start_y, end_x, end_y are required when dragging by coordinates.");

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);

            double sx = 0, sy = 0, ex = 0, ey = 0;
            ILocator? startLocator = null, endLocator = null;
            string fromText, toText;
            if (hasRefs)
            {
                // Resolved now so a stale ref is reported before anything is pressed, but measured
                // only when the pointer is about to go there: each end is scrolled into view first,
                // and scrolling to the drop target can move the element being dragged.
                var snapshots = pageService.GetSnapshotService(page);
                var scope = pageService.GetActiveFrame();
                startLocator = snapshots.ResolveRef(start_ref!, scope);
                endLocator = snapshots.ResolveRef(end_ref!, scope);
                fromText = start_ref!;
                toText = end_ref!;
            }
            else
            {
                (sx, sy) = (start_x!.Value, start_y!.Value);
                (ex, ey) = (end_x!.Value, end_y!.Value);
                fromText = $"({sx}, {sy})";
                toText = $"({ex}, {ey})";
            }

            var moveSteps = Math.Max(1, steps ?? 10);

            return await ActionRunner.RunAsync(pageService, page, cancellationToken, async token =>
            {
                if (startLocator is not null)
                    (sx, sy) = await CenterOfAsync(startLocator, start_ref!, pageService.ActionTimeout).ConfigureAwait(false);

                await page.Mouse.MoveAsync(sx, sy).ConfigureAwait(false);
                await page.Mouse.DownAsync().ConfigureAwait(false);

                try
                {
                    if (hold_ms is > 0)
                        await Task.Delay(hold_ms.Value, token).ConfigureAwait(false);

                    if (endLocator is not null)
                        (ex, ey) = await CenterOfAsync(endLocator, end_ref!, pageService.ActionTimeout).ConfigureAwait(false);

                    await page.Mouse.MoveAsync(ex, ey, new MouseMoveOptions(Steps: moveSteps)).ConfigureAwait(false);
                }
                catch
                {
                    // A drop target that cannot be reached must not leave the button held down for
                    // whatever the agent does next.
                    await page.Mouse.UpAsync().ConfigureAwait(false);
                    throw;
                }

                await page.Mouse.UpAsync().ConfigureAwait(false);

                return ToolResultHelper.Text($"Dragged {fromText} to {toText}");
            }).ConfigureAwait(false);
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
            return ToolResultHelper.Error($"Drag failed: {ex.Message}");
        }
    }

    private static async Task<CallToolResult> MoveCoreAsync(
        ActivePageService pageService, int x, int y, string okText, CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            return await ActionRunner.RunAsync(pageService, page, cancellationToken, async _ =>
            {
                await page.Mouse.MoveAsync(x, y).ConfigureAwait(false);
                return ToolResultHelper.Text(okText);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Pointer move to ({x}, {y}) failed: {ex.Message}");
        }
    }

    // Scrolls the element into view first, since a point outside the viewport is one the pointer
    // cannot reach.
    private static async Task<(double X, double Y)> CenterOfAsync(ILocator locator, string @ref, double? timeout)
    {
        await locator.ScrollIntoViewIfNeededAsync(timeout).ConfigureAwait(false);
        var box = await locator.BoundingBoxAsync(timeout).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Element {@ref} has no visible bounding box.");
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }
}
