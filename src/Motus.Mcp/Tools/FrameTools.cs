using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tools for naming and scoping to the frames of a page. A page snapshot already prints what is
/// inside each frame, so reading and clicking need nothing from here; what still needs a frame
/// named is evaluating script in it, waiting for text in it, and reading one frame on its own.
/// </summary>
/// <remarks>
/// Selection works like tabs and contexts: <c>frame_select</c> sets the scope and the calls that
/// follow act inside it, rather than every call repeating a frame argument. Scope covers
/// <c>snapshot</c>, <c>evaluate</c> and the text waits; the refs a scoped snapshot hands out keep
/// working for every interaction tool afterwards. It resets on navigation and on switching tab or
/// context, since the frame it named is gone by then.
///
/// The index a frame has here is the index its refs carry in a page snapshot, so <c>f2e5</c> and
/// <c>frame_select 2</c> name the same document.
///
/// The coordinate tools stay in page coordinates whatever is selected. Their input is dispatched at
/// the page level and the browser decides for itself which frame is under the point.
/// </remarks>
[McpServerToolType]
public sealed class FrameTools
{
    [McpServerTool(Name = "frame_list", Title = "List frames", Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Lists the frames of the active page, each with its zero-based index, nesting depth, URL, and "
        + "name. Index 0 is the page itself and a frame is listed after the frame that holds it, though frames at "
        + "the same level come in the order the browser reports them rather than the order they appear in the "
        + "page. The scoped frame is marked with an asterisk. The index is the one a page snapshot prints as "
        + "[frame=N] and puts in front of the refs inside it.")]
    public static async Task<CallToolResult> FrameListAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var frames = await pageService.ListFramesAsync(cancellationToken).ConfigureAwait(false);
            var active = pageService.GetActiveFrame();

            var builder = new StringBuilder();
            for (var i = 0; i < frames.Count; i++)
            {
                var (frame, depth) = (frames[i].Frame, frames[i].Depth);
                var scoped = active is null ? i == 0 : ReferenceEquals(frame, active);

                builder.Append(scoped ? "* " : "  ")
                    .Append('[').Append(i).Append("] ")
                    .Append(' ', depth * 2)
                    .Append(i == 0 ? "(page) " : string.Empty)
                    .Append(frame.Url);

                if (!string.IsNullOrEmpty(frame.Name))
                    builder.Append(" | ").Append(frame.Name);

                builder.AppendLine();
            }

            if (frames.Count == 1)
                builder.AppendLine("This page has no frames beyond itself.");

            return ToolResultHelper.Text(builder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Listing frames failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "frame_select", Title = "Select a frame", Destructive = false)]
    [Description("Scopes snapshot, evaluate, and the text waits to the frame at the given zero-based index. "
        + "Clicking and typing inside a frame need no scope: a page snapshot prints every frame and its refs "
        + "reach into them. Index 0 returns to the page. Indices come from frame_list. Take a snapshot "
        + "afterwards: refs from the previous scope do not carry over.")]
    public static async Task<CallToolResult> FrameSelectAsync(
        [Description("Zero-based index of the frame to scope to, from frame_list. 0 is the page itself.")] int index,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var frame = await pageService.SelectFrameAsync(index, cancellationToken).ConfigureAwait(false);

            return ToolResultHelper.Text(index == 0
                ? $"Scoped back to the page: {frame.Url}"
                : $"Scoped to frame {index}: {frame.Url}. Take a snapshot to address its elements.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error(ex.Message);
        }
    }
}
