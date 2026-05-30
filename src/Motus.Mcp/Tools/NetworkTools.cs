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
        [Description("HTTP status code to return. Defaults to 200.")] int? status,
        [Description("Response body to return.")] string? body,
        [Description("Content-Type of the response, e.g. application/json.")] string? content_type,
        [Description("Additional response headers as name/value pairs.")] Dictionary<string, string>? headers,
        ActivePageService pageService,
        NetworkService networkService,
        CancellationToken cancellationToken)
    {
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
        [Description("Optional error code, e.g. aborted, accessdenied, connectionrefused, blockedbyclient.")] string? error_code,
        ActivePageService pageService,
        NetworkService networkService,
        CancellationToken cancellationToken)
    {
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
        [Description("Override the request URL.")] string? url,
        [Description("Override the HTTP method, e.g. POST.")] string? method,
        [Description("Override or add request headers as name/value pairs.")] Dictionary<string, string>? headers,
        [Description("Override the request body.")] string? post_data,
        ActivePageService pageService,
        NetworkService networkService,
        CancellationToken cancellationToken)
    {
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

    [McpServerTool(Name = "network_requests", Title = "Read the request log", Destructive = false)]
    [Description("Returns the requests the active tab has finished since the last read, then clears the log. Each "
        + "line is METHOD STATUS URL (resource type); a failed or blocked request shows FAILED.")]
    public static CallToolResult NetworkRequests(
        NetworkService networkService,
        CancellationToken cancellationToken)
    {
        var entries = networkService.DrainRequests();
        if (entries.Count == 0)
            return ToolResultHelper.Text("No requests have been logged since the last read.");

        var builder = new StringBuilder();
        foreach (var entry in entries)
            builder.AppendLine(entry.ToString());

        return ToolResultHelper.Text(builder.ToString().TrimEnd());
    }
}
