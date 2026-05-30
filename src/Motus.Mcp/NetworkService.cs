using System.Runtime.CompilerServices;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Holds the network surface for an MCP server session: the mock rules registered on
/// each context, and a log of the requests the active page made. Tool calls arrive as
/// individually stateless messages, so both the rules and the log have to live here
/// between the call that sets them up and the calls that use or read them.
/// </summary>
/// <remarks>
/// Mock rules are context-level: a rule is registered through
/// <see cref="IBrowserContext.RouteAsync"/>, so it applies to every tab in the
/// context and survives navigation and tab switches, and a closed-and-collected
/// context drops its rules automatically through the weak table. The request log is
/// page-level: <see cref="SubscribePage"/> follows the active tab exactly as the
/// dialog and console subscriptions do, and the bounded log drains on read.
/// </remarks>
public sealed class NetworkService
{
    private const int Capacity = 250;

    private readonly ConditionalWeakTable<IBrowserContext, RouteTable> _routes = new();

    private readonly object _logLock = new();
    private readonly Queue<NetworkEntry> _log = new();

    private IPage? _subscribedPage;

    // --- route mocking (context-level) ---

    /// <summary>Registers (or replaces) a rule that fulfills matching requests with a custom response.</summary>
    public Task RegisterFulfillAsync(
        IBrowserContext context, string pattern, RouteFulfillOptions options, CancellationToken cancellationToken = default)
        => RegisterAsync(context, pattern, new RouteRule(RuleKind.Fulfill, options, null, null), cancellationToken);

    /// <summary>Registers (or replaces) a rule that aborts matching requests.</summary>
    public Task RegisterAbortAsync(
        IBrowserContext context, string pattern, string? errorCode, CancellationToken cancellationToken = default)
        => RegisterAsync(context, pattern, new RouteRule(RuleKind.Abort, null, errorCode, null), cancellationToken);

    /// <summary>Registers (or replaces) a rule that lets matching requests continue with overrides.</summary>
    public Task RegisterContinueAsync(
        IBrowserContext context, string pattern, RouteContinueOptions options, CancellationToken cancellationToken = default)
        => RegisterAsync(context, pattern, new RouteRule(RuleKind.Continue, null, null, options), cancellationToken);

    private async Task RegisterAsync(IBrowserContext context, string pattern, RouteRule rule, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        var table = _routes.GetValue(context, static _ => new RouteTable());

        bool alreadyRouted;
        lock (table.Gate)
        {
            alreadyRouted = table.Rules.ContainsKey(pattern);
            table.Rules[pattern] = rule;
        }

        // Register the handler once per pattern. A re-registration replaces the rule
        // in the table, and the already-attached handler reads the latest rule, so a
        // pattern is never routed twice.
        if (!alreadyRouted)
            await context.RouteAsync(pattern, route => ApplyAsync(table, pattern, route)).ConfigureAwait(false);
    }

    private static async Task ApplyAsync(RouteTable table, string pattern, IRoute route)
    {
        RouteRule? rule;
        lock (table.Gate)
            table.Rules.TryGetValue(pattern, out rule);

        if (rule is null)
        {
            await route.ContinueAsync().ConfigureAwait(false);
            return;
        }

        switch (rule.Kind)
        {
            case RuleKind.Fulfill:
                await route.FulfillAsync(rule.Fulfill).ConfigureAwait(false);
                break;
            case RuleKind.Abort:
                await route.AbortAsync(rule.AbortCode).ConfigureAwait(false);
                break;
            default:
                await route.ContinueAsync(rule.Continue).ConfigureAwait(false);
                break;
        }
    }

    /// <summary>
    /// Removes the rule for a pattern and unregisters it from the context. Returns
    /// whether a rule was registered for the pattern.
    /// </summary>
    public async Task<bool> UnrouteAsync(IBrowserContext context, string pattern, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        if (!_routes.TryGetValue(context, out var table))
            return false;

        bool removed;
        lock (table.Gate)
            removed = table.Rules.Remove(pattern);

        if (removed)
            await context.UnrouteAsync(pattern).ConfigureAwait(false);

        return removed;
    }

    /// <summary>Lists the rules registered on the context, in registration order.</summary>
    public IReadOnlyList<RouteInfo> ListRoutes(IBrowserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!_routes.TryGetValue(context, out var table))
            return [];

        lock (table.Gate)
            return table.Rules.Select(kv => new RouteInfo(kv.Key, kv.Value.Kind.ToString())).ToArray();
    }

    // --- request log (page-following) ---

    /// <summary>
    /// Attaches to the given page's response and request-failed events, detaching
    /// from any previously subscribed page. A repeat call for the same page is a
    /// no-op.
    /// </summary>
    public void SubscribePage(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (ReferenceEquals(_subscribedPage, page))
            return;

        if (_subscribedPage is not null)
        {
            _subscribedPage.Response -= OnResponse;
            _subscribedPage.RequestFailed -= OnRequestFailed;
        }

        _subscribedPage = page;
        page.Response += OnResponse;
        page.RequestFailed += OnRequestFailed;
    }

    /// <summary>Returns the logged requests in arrival order and clears the log.</summary>
    public IReadOnlyList<NetworkEntry> DrainRequests()
    {
        lock (_logLock)
        {
            var drained = _log.ToArray();
            _log.Clear();
            return drained;
        }
    }

    private void OnResponse(object? sender, ResponseEventArgs e)
    {
        var request = e.Response.Request;
        Add(new NetworkEntry(request.Method, e.Response.Status, e.Response.Url, request.ResourceType, Failed: false));
    }

    private void OnRequestFailed(object? sender, RequestEventArgs e)
        => Add(new NetworkEntry(e.Request.Method, Status: null, e.Request.Url, e.Request.ResourceType, Failed: true));

    private void Add(NetworkEntry entry)
    {
        lock (_logLock)
        {
            if (_log.Count >= Capacity)
                _log.Dequeue();
            _log.Enqueue(entry);
        }
    }

    private enum RuleKind
    {
        Fulfill,
        Abort,
        Continue,
    }

    private sealed record RouteRule(
        RuleKind Kind, RouteFulfillOptions? Fulfill, string? AbortCode, RouteContinueOptions? Continue);

    private sealed class RouteTable
    {
        public object Gate { get; } = new();

        public Dictionary<string, RouteRule> Rules { get; } = new(StringComparer.Ordinal);
    }
}

/// <summary>A registered route rule, as reported by <see cref="NetworkService.ListRoutes"/>.</summary>
/// <param name="Pattern">The URL pattern the rule matches.</param>
/// <param name="Kind">The action the rule takes (fulfill, abort, or continue).</param>
public sealed record RouteInfo(string Pattern, string Kind);

/// <summary>A single logged request/response.</summary>
/// <param name="Method">The HTTP method.</param>
/// <param name="Status">The response status code, or null when the request failed.</param>
/// <param name="Url">The request URL.</param>
/// <param name="ResourceType">The resource type (e.g. document, script, fetch).</param>
/// <param name="Failed">Whether the request failed or was aborted.</param>
public sealed record NetworkEntry(string Method, int? Status, string Url, string ResourceType, bool Failed)
{
    /// <summary>Renders the entry as a single <c>METHOD STATUS URL (type)</c> line.</summary>
    public override string ToString()
    {
        var status = Failed ? "FAILED" : Status.ToString();
        var type = string.IsNullOrEmpty(ResourceType) ? string.Empty : $" ({ResourceType})";
        return $"{Method} {status} {Url}{type}";
    }
}
