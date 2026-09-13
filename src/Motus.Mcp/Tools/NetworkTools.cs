using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Tools for the network: mocking requests on the active context and reading the log
/// of requests the active tab has made. A mock rule applies to every tab in the
/// active context and survives navigation; the request log follows the active tab.
/// </summary>
/// <remarks>
/// Like the other tools, failures are returned as a result with
/// <see cref="CallToolResult.IsError"/> set and a message the model can act on,
/// rather than thrown. Patterns are matched as an exact string, a glob when they
/// contain <c>*</c>, or a substring otherwise.
/// </remarks>
[McpServerToolType]
public sealed class NetworkTools
{
    [McpServerTool(Name = "route_fulfill", Title = "Mock a response", Destructive = true)]
    [Description("Intercepts requests matching the URL pattern on the active context and answers them with a mock "
        + "response instead of hitting the network. Re-registering the same pattern replaces its rule.")]
    public static async Task<CallToolResult> RouteFulfillAsync(
        [Description("URL pattern to match: an exact URL, a glob with *, or a substring.")] string url_pattern,
        ActivePageService pageService,
        NetworkService networkService,
        CancellationToken cancellationToken,
        [Description("HTTP status code to return. Defaults to 200.")] int? status = null,
        [Description("Response body to return.")] string? body = null,
        [Description("Content-Type of the response, e.g. application/json.")] string? content_type = null,
        [Description("Additional response headers as name/value pairs.")] Dictionary<string, string>? headers = null,
        SecurityPolicy? policy = null)
    {
        if (ToolArguments.Missing("url_pattern", url_pattern) is { } missing)
            return missing;

        if ((policy ?? SecurityPolicy.Default).RefuseHeaders(headers) is { } refusal)
            return ToolResultHelper.Error(refusal);

        try
        {
            var context = await pageService.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
            var options = new RouteFulfillOptions
            {
                Status = status,
                Body = body,
                ContentType = content_type,
                Headers = headers is { Count: > 0 } ? headers : null,
            };
            await networkService.RegisterFulfillAsync(context, url_pattern, options, cancellationToken).ConfigureAwait(false);
            return ToolResultHelper.Text($"Mocking '{url_pattern}' with status {status ?? 200}.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Mocking '{url_pattern}' failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "route_abort", Title = "Block requests", Destructive = true)]
    [Description("Intercepts requests matching the URL pattern on the active context and aborts them, so the page "
        + "sees a failed request. Re-registering the same pattern replaces its rule.")]
    public static async Task<CallToolResult> RouteAbortAsync(
        [Description("URL pattern to match: an exact URL, a glob with *, or a substring.")] string url_pattern,
        ActivePageService pageService,
        NetworkService networkService,
        CancellationToken cancellationToken,
        [Description("Optional error code, e.g. aborted, accessdenied, connectionrefused, blockedbyclient.")] string? error_code = null)
    {
        if (ToolArguments.Missing("url_pattern", url_pattern) is { } missing)
            return missing;

        try
        {
            var context = await pageService.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
            await networkService.RegisterAbortAsync(context, url_pattern, error_code, cancellationToken).ConfigureAwait(false);
            return ToolResultHelper.Text($"Blocking '{url_pattern}'.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Blocking '{url_pattern}' failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "route_continue", Title = "Override requests", Destructive = true)]
    [Description("Intercepts requests matching the URL pattern on the active context and lets them continue with "
        + "the given overrides applied (URL, method, headers, or body). Re-registering the same pattern replaces "
        + "its rule.")]
    public static async Task<CallToolResult> RouteContinueAsync(
        [Description("URL pattern to match: an exact URL, a glob with *, or a substring.")] string url_pattern,
        ActivePageService pageService,
        NetworkService networkService,
        CancellationToken cancellationToken,
        [Description("Override the request URL.")] string? url = null,
        [Description("Override the HTTP method, e.g. POST.")] string? method = null,
        [Description("Override or add request headers as name/value pairs.")] Dictionary<string, string>? headers = null,
        [Description("Override the request body.")] string? post_data = null,
        SecurityPolicy? policy = null)
    {
        if (ToolArguments.Missing("url_pattern", url_pattern) is { } missing)
            return missing;

        if ((policy ?? SecurityPolicy.Default).RefuseUrl(url) is { } refusal)
            return ToolResultHelper.Error(refusal);

        try
        {
            var context = await pageService.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
            var options = new RouteContinueOptions(
                Url: url,
                Method: method,
                Headers: headers is { Count: > 0 } ? headers : null,
                PostData: post_data);
            await networkService.RegisterContinueAsync(context, url_pattern, options, cancellationToken).ConfigureAwait(false);
            return ToolResultHelper.Text($"Continuing '{url_pattern}' with overrides.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Overriding '{url_pattern}' failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "unroute", Title = "Remove a mock", Destructive = false)]
    [Description("Removes the mock rule for the URL pattern on the active context, so matching requests hit the "
        + "network again. Reports when no rule was registered for the pattern.")]
    public static async Task<CallToolResult> UnrouteAsync(
        [Description("The URL pattern whose rule to remove.")] string url_pattern,
        ActivePageService pageService,
        NetworkService networkService,
        CancellationToken cancellationToken)
    {
        if (ToolArguments.Missing("url_pattern", url_pattern) is { } missing)
            return missing;

        try
        {
            var context = await pageService.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
            var removed = await networkService.UnrouteAsync(context, url_pattern, cancellationToken).ConfigureAwait(false);
            return ToolResultHelper.Text(removed
                ? $"Removed the mock for '{url_pattern}'."
                : $"No mock was registered for '{url_pattern}'.");
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Removing the mock for '{url_pattern}' failed: {ex.Message}");
        }
    }

    [McpServerTool(Name = "route_list", Title = "List mocks", Destructive = false, ReadOnly = true, Idempotent = true)]
    [Description("Lists the mock rules registered on the active context, each with its pattern and action.")]
    public static async Task<CallToolResult> RouteListAsync(
        ActivePageService pageService,
        NetworkService networkService,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await pageService.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
            var routes = networkService.ListRoutes(context);
            if (routes.Count == 0)
                return ToolResultHelper.Text("No mocks are registered on the active context.");

            var builder = new StringBuilder();
            foreach (var route in routes)
                builder.Append(route.Pattern).Append(" -> ").AppendLine(route.Kind);

            return ToolResultHelper.Text(builder.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Listing mocks failed: {ex.Message}");
        }
    }

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
