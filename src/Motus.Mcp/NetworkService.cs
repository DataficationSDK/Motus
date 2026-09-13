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
/// dialog and console subscriptions do, and the bounded log is read by cursor rather
/// than emptied, so a read that never reached the agent can be made again.
/// </remarks>
public sealed class NetworkService
{
    private const int Capacity = 250;

    /// <summary>How much of a request body is kept. A form post is small; an upload is not.</summary>
    private const int PostDataLimit = 2000;

    /// <summary>How long a single response-body fetch may take before it is given up on.</summary>
    private static readonly TimeSpan BodyTimeout = TimeSpan.FromSeconds(5);

    private readonly ConditionalWeakTable<IBrowserContext, RouteTable> _routes = new();

    private readonly object _logLock = new();
    private readonly Queue<LoggedRequest> _log = new();

    private long _next = 1;
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

    /// <summary>
    /// The sequence number the next logged request will be given.
    /// </summary>
    public long NextSequence
    {
        get { lock (_logLock) return _next; }
    }

    /// <summary>
    /// Returns the logged requests from <paramref name="since"/> onwards in arrival order,
    /// leaving the log as it is.
    /// </summary>
    /// <param name="since">
    /// The sequence number to read from, or null for everything the log still holds.
    /// </param>
    public LogSlice<NetworkEntry> ReadRequests(long? since = null)
    {
        lock (_logLock)
        {
            var from = since is { } cursor && cursor > 1 ? cursor : 1;
            var oldest = _log.Count == 0 ? _next : _log.Peek().Entry.Sequence;
            var dropped = oldest > from ? (int)(oldest - from) : 0;

            var entries = new List<NetworkEntry>();
            foreach (var logged in _log)
            {
                if (logged.Entry.Sequence >= from)
                    entries.Add(logged.Entry);
            }

            return new LogSlice<NetworkEntry>(entries, _next, dropped);
        }
    }

    /// <summary>
    /// Returns the logged request with the given sequence number, or null when the log never held
    /// it or has since evicted it.
    /// </summary>
    public NetworkEntry? FindRequest(long sequence)
    {
        lock (_logLock)
        {
            foreach (var logged in _log)
            {
                if (logged.Entry.Sequence == sequence)
                    return logged.Entry;
            }

            return null;
        }
    }

    /// <summary>
    /// Reads the response body of a logged request from the browser, or returns null when the log
    /// has no response for that sequence number and when the browser can no longer produce one.
    /// </summary>
    /// <remarks>
    /// The body is not captured when the request is logged, because a page that downloads a file
    /// would put the file in the server's memory for as long as the log held the entry. It is
    /// fetched on request instead, which is why it can fail: the browser keeps response data for a
    /// while and then evicts it, and everything from a document the page has navigated away from
    /// goes at once. The fetch is bounded, since the page it belongs to may have stopped answering
    /// since the request was made.
    /// </remarks>
    public async Task<string?> TryReadBodyAsync(long sequence, CancellationToken cancellationToken = default)
    {
        IResponse? response;
        lock (_logLock)
            response = _log.FirstOrDefault(logged => logged.Entry.Sequence == sequence)?.Response;

        if (response is null)
            return null;

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(BodyTimeout);

        try
        {
            return await response.TextAsync(bounded.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Every failure here means the same thing to the caller: the browser did not hand the
            // body over. Which way it declined is not something an agent can act on differently.
            return null;
        }
    }

    private void OnResponse(object? sender, ResponseEventArgs e)
    {
        var request = e.Response.Request;
        var entry = new NetworkEntry(
            Sequence: 0, request.Method, e.Response.Status, e.Response.Url, request.ResourceType, Failed: false)
        {
            RequestHeaders = Capture(request.Headers),
            ResponseHeaders = Capture(e.Response.Headers),
            PostData = CapturePostData(request),
        };

        Add(entry, e.Response);
    }

    private void OnRequestFailed(object? sender, RequestEventArgs e)
    {
        var entry = new NetworkEntry(
            Sequence: 0, e.Request.Method, Status: null, e.Request.Url, e.Request.ResourceType, Failed: true)
        {
            RequestHeaders = Capture(e.Request.Headers),
            PostData = CapturePostData(e.Request),
        };

        Add(entry, response: null);
    }

    /// <summary>
    /// Copies a header collection as it stands. Headers arrive with the event and are already in
    /// memory, so this costs nothing at capture time and means a later read does not depend on
    /// the request object still being meaningful.
    /// </summary>
    private static IReadOnlyList<KeyValuePair<string, string>> Capture(IHeaderCollection? headers)
    {
        if (headers is null)
            return [];

        try
        {
            var copied = new List<KeyValuePair<string, string>>();
            foreach (var header in headers)
                copied.Add(new KeyValuePair<string, string>(header.Key, string.Join(", ", header.Value)));

            return copied;
        }
        catch (Exception)
        {
            // A transport that cannot describe the headers is not a reason to lose the entry.
            return [];
        }
    }

    private static string? CapturePostData(IRequest request)
    {
        try
        {
            var data = request.PostData;
            if (string.IsNullOrEmpty(data))
                return null;

            return data.Length <= PostDataLimit ? data : data[..PostDataLimit] + "...";
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Add(NetworkEntry entry, IResponse? response)
    {
        lock (_logLock)
        {
            if (_log.Count >= Capacity)
                _log.Dequeue();
            _log.Enqueue(new LoggedRequest(entry with { Sequence = _next++ }, response));
        }
    }

    /// <summary>
    /// A log entry and the response object it came from. The response is held apart from the entry
    /// because it is a live browser handle rather than something to print: it is what makes a body
    /// readable later, and it is not part of what a reader is handed.
    /// </summary>
    private sealed record LoggedRequest(NetworkEntry Entry, IResponse? Response);

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
/// <param name="Sequence">The entry's position in the log, counting from one and never reused.</param>
/// <param name="Method">The HTTP method.</param>
/// <param name="Status">The response status code, or null when the request failed.</param>
/// <param name="Url">The request URL.</param>
/// <param name="ResourceType">The resource type (e.g. document, script, fetch).</param>
/// <param name="Failed">Whether the request failed or was aborted.</param>
public sealed record NetworkEntry(
    long Sequence, string Method, int? Status, string Url, string ResourceType, bool Failed)
{
    /// <summary>The headers the browser sent, as they were at the moment the entry was logged.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> RequestHeaders { get; init; } = [];

    /// <summary>The headers that came back, empty for a request that never got a response.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; init; } = [];

    /// <summary>The request body, truncated when it is long, or null when the request had none.</summary>
    public string? PostData { get; init; }

    /// <summary>The value of a response header, or null when the response did not carry it.</summary>
    public string? ResponseHeader(string name)
    {
        foreach (var header in ResponseHeaders)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                return header.Value;
        }

        return null;
    }

    /// <summary>Renders the entry as a single <c>METHOD STATUS URL (type)</c> line.</summary>
    public override string ToString()
    {
        var status = Failed ? "FAILED" : Status.ToString();
        var type = string.IsNullOrEmpty(ResourceType) ? string.Empty : $" ({ResourceType})";
        return $"{Method} {status} {Url}{type}";
    }
}
