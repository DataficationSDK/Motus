using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tools for looking inside the frames of a page. A page snapshot describes each <c>iframe</c>
/// element but not what is inside it, so a frame has to be selected before its content can be
/// perceived or acted on.
/// </summary>
/// <remarks>
/// Selection works like tabs and contexts: <c>frame_select</c> sets the scope and the calls that
/// follow act inside it, rather than every call repeating a frame argument. Scope covers
/// <c>snapshot</c>, <c>evaluate</c> and the text waits; the refs a scoped snapshot hands out keep
/// working for every interaction tool afterwards. It resets on navigation and on switching tab or
/// context, since the frame it named is gone by then.
///
/// The coordinate tools stay in page coordinates whatever is selected. Their input is dispatched at
/// the page level and the browser decides for itself which frame is under the point.
/// </remarks>
[McpServerToolType]
public sealed class FrameTools
{
    [McpServerTool(Name = "frame_list", Title = "List frames", Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Lists the frames of the active page in document order, each with its zero-based index, nesting "
        + "depth, URL, and name. Index 0 is the page itself. The scoped frame is marked with an asterisk.")]
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
    [Description("Scopes snapshot, evaluate, and the text waits to the frame at the given zero-based index, so "
        + "its content becomes addressable. Index 0 returns to the page. Indices come from frame_list. Take a "
        + "snapshot afterwards: refs from the previous scope do not carry over.")]
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
