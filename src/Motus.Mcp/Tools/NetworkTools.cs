using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tools for reading the log of requests the active tab has made: the list of what it
/// fetched, and the full detail of any one of them. The log follows the active tab.
/// </summary>
/// <remarks>
/// Like the other tools, failures are returned as a result with
/// <see cref="CallToolResult.IsError"/> set and a message the model can act on,
/// rather than thrown. Reading a log never clears it, so the same read can be made
/// again and two readers do not take entries from each other.
/// </remarks>
[McpServerToolType]
public sealed class NetworkTools
{
    [McpServerTool(Name = "network_requests", Title = "Read the request log", Destructive = false, ReadOnly = true)]
    [Description("Returns the requests the active tab has finished. Each line is [n] METHOD STATUS URL (resource "
        + "type), where n is the sequence number network_request takes; a failed or blocked request shows FAILED. "
        + "Reading does not clear the log, so the same call can be made again. The last line is next=N: pass that "
        + "as since to read only what arrives after this read.")]
    public static CallToolResult NetworkRequests(
        NetworkService networkService,
        CancellationToken cancellationToken,
        [Description("Return only entries from this sequence number onwards. Omit to read everything the log holds.")]
        long? since = null)
    {
        var slice = networkService.ReadRequests(since);
        return ToolResultHelper.Text(LogText.Render(
            slice,
            entry => $"[{entry.Sequence}] {entry}",
            since is null
                ? "No requests have been logged."
                : $"No requests have been logged since {since}."));
    }

    [McpServerTool(Name = "network_request", Title = "Read one request", Destructive = false, ReadOnly = true,
        Idempotent = true)]
    [Description("Returns the detail the log holds for one request: its method, status, URL, resource type, "
        + "request and response headers, any request body, and the response body when the browser can still "
        + "produce it. The sequence number is the [n] that network_requests prints in front of each line.")]
    public static async Task<CallToolResult> NetworkRequestAsync(
        [Description("The sequence number of the request, as shown in square brackets by network_requests.")]
        long sequence,
        NetworkService networkService,
        CancellationToken cancellationToken)
    {
        var entry = networkService.FindRequest(sequence);
        if (entry is null)
            return ToolResultHelper.Error(
                $"No request with sequence {sequence} is in the log. Call network_requests to see what it holds.");

        var builder = new StringBuilder();
        builder.Append('[').Append(entry.Sequence).Append("] ").AppendLine(entry.ToString());
        AppendHeaders(builder, "Request headers", entry.RequestHeaders);
        AppendHeaders(builder, "Response headers", entry.ResponseHeaders);

        if (entry.PostData is { Length: > 0 } postData)
            builder.AppendLine("Request body:").AppendLine(postData);

        builder.Append("Response body: ")
            .AppendLine(await DescribeBodyAsync(networkService, entry, cancellationToken).ConfigureAwait(false));

        return ToolResultHelper.Text(builder.ToString().TrimEnd());
    }

    private static void AppendHeaders(
        StringBuilder builder, string label, IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (headers.Count == 0)
            return;

        builder.Append(label).AppendLine(":");
        foreach (var header in headers)
            builder.Append("  ").Append(header.Key).Append(": ").AppendLine(header.Value);
    }

    /// <summary>
    /// Describes the response body, or says why it is not being printed. A body is worth fetching
    /// only when it is text the agent can read and small enough to be worth the tokens; the
    /// browser also evicts response data, so the answer may simply be that it is gone.
    /// </summary>
    private static async Task<string> DescribeBodyAsync(
        NetworkService networkService, NetworkEntry entry, CancellationToken cancellationToken)
    {
        if (entry.Failed)
            return "the request failed, so there is none.";

        var contentType = entry.ResponseHeader("content-type") ?? string.Empty;
        if (contentType.Length > 0 && !IsText(contentType))
            return $"not shown, because it is {contentType}.";

        if (long.TryParse(entry.ResponseHeader("content-length"), out var length) && length > BodyLimit)
            return $"not shown, because it is {length} bytes.";

        var body = await networkService.TryReadBodyAsync(entry.Sequence, cancellationToken).ConfigureAwait(false);
        if (body is null)
            return "the browser no longer has it. Response data is evicted, and a navigation drops it at once.";

        return body.Length <= BodyLimit
            ? Environment.NewLine + body
            : Environment.NewLine + body[..BodyLimit] + "... (truncated)";
    }

    /// <summary>How much of a response body is printed, and the size above which it is not fetched.</summary>
    private const int BodyLimit = 4000;

    private static bool IsText(string contentType)
        => contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("javascript", StringComparison.OrdinalIgnoreCase)
            || contentType.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);
}
