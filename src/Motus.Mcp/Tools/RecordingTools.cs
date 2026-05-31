using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Captures diagnostic artifacts from the session: a DOM-snapshot trace of the
/// active context and an HTTP archive (HAR) of the active page's network traffic.
/// Each is a start/stop pair; stopping writes the artifact to a file and returns
/// its path.
/// </summary>
[McpServerToolType]
public sealed class RecordingTools
{
    [McpServerTool(Name = "trace_start", Title = "Start trace recording", Destructive = false, ReadOnly = false)]
    [Description("Begins recording a trace of the active browser context. Call trace_stop to write "
        + "the trace to a file. Optionally capture screenshots and DOM snapshots, which make the "
        + "trace richer but larger.")]
    public static async Task<CallToolResult> TraceStartAsync(
        [Description("Capture screenshots throughout the trace. Defaults to true.")] bool? screenshots,
        [Description("Capture DOM snapshots throughout the trace. Defaults to true.")] bool? snapshots,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await pageService.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = screenshots ?? true,
                Snapshots = snapshots ?? true,
            }).ConfigureAwait(false);

            return ToolResultHelper.Text("Trace recording started. Call trace_stop to write it to a file.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Starting the trace failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "trace_stop", Title = "Stop trace recording", Destructive = false, ReadOnly = false)]
    [Description("Stops the trace started by trace_start and writes it to a ZIP file. Returns the file "
        + "path. Provide a path to choose where it is written, or omit it for an auto-generated path "
        + "under the temporary directory.")]
    public static async Task<CallToolResult> TraceStopAsync(
        [Description("Where to write the trace ZIP. Omit for an auto-generated path.")] string? path,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolved = ResolvePath(path, "motus-trace", ".zip");
            var context = await pageService.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
            await context.Tracing.StopAsync(new TracingStopOptions { Path = resolved }).ConfigureAwait(false);

            return ToolResultHelper.Text($"Trace written to {resolved}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Stopping the trace failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "har_start", Title = "Start HAR recording", Destructive = false, ReadOnly = false)]
    [Description("Begins recording the active page's network traffic. Call har_stop to write the "
        + "captured requests and responses to an HTTP archive (HAR) file.")]
    public static async Task<CallToolResult> HarStartAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            await page.StartHarRecordingAsync(cancellationToken).ConfigureAwait(false);

            return ToolResultHelper.Text("HAR recording started. Call har_stop to write it to a file.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Starting HAR recording failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "har_stop", Title = "Stop HAR recording", Destructive = false, ReadOnly = false)]
    [Description("Stops the recording started by har_start and writes the captured traffic to an HTTP "
        + "archive (HAR) file. Returns the file path. Provide a path to choose where it is written, or "
        + "omit it for an auto-generated path under the temporary directory.")]
    public static async Task<CallToolResult> HarStopAsync(
        [Description("Where to write the HAR file. Omit for an auto-generated path.")] string? path,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolved = ResolvePath(path, "motus", ".har");
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            await page.StopHarRecordingAsync(resolved, cancellationToken).ConfigureAwait(false);

            return ToolResultHelper.Text($"HAR written to {resolved}");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Stopping HAR recording failed: {ex.Message}");
        }
    }

    private static string ResolvePath(string? path, string prefix, string extension)
    {
        if (!string.IsNullOrWhiteSpace(path))
            return path;

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var unique = Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(Path.GetTempPath(), $"{prefix}-{stamp}-{unique}{extension}");
    }
}
