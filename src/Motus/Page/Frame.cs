using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents a frame within a page (including the main frame).
/// </summary>
internal sealed class Frame : IFrame
{
    private readonly Page _page;

    internal Frame(Page page, string id, string? parentFrameId)
    {
        _page = page;
        Id = id;
        ParentFrameId = parentFrameId;
    }

    internal string Id { get; }

    internal string? ParentFrameId { get; private set; }

    internal bool IsMainFrame => ParentFrameId is null;

    /// <summary>
    /// Records the parent when it was not known at construction.
    /// </summary>
    /// <remarks>
    /// A frame in its own process is reported both by the target it owns and by the page that
    /// embeds it, in either order, and the two do not necessarily carry the same detail. Whichever
    /// arrives with a parent supplies it; a parent already recorded is never overwritten.
    /// </remarks>
    internal void EnsureParent(string parentFrameId) => ParentFrameId ??= parentFrameId;

    internal void MarkDetached() => IsDetached = true;

    public IPage Page => _page;

    public bool IsDetached { get; private set; }

    public IFrame? ParentFrame =>
        ParentFrameId is not null && _page.TryGetFrame(ParentFrameId, out var parent)
            ? parent
            : null;

    public string Name { get; internal set; } = string.Empty;

    public string Url { get; internal set; } = string.Empty;

    public IReadOnlyList<IFrame> ChildFrames =>
        _page.GetChildFrames(Id);

    public async Task<T> EvaluateAsync<T>(string expression, object? arg = null) =>
        await _page.EvaluateInFrameAsync<T>(Id, expression, arg).ConfigureAwait(false);

    public async Task<T> EvaluateAsync<T>(string expression, object? arg, EvaluateOptions options) =>
        await _page.EvaluateInFrameAsync<T>(Id, expression, arg, options.World).ConfigureAwait(false);

    public async Task<T> WaitForFunctionAsync<T>(string expression, object? arg = null, double? timeout = null) =>
        await _page.WaitForFunctionInFrameAsync<T>(Id, expression, arg, timeout).ConfigureAwait(false);

    public async Task<string> ContentAsync() =>
        await EvaluateAsync<string>("document.documentElement.outerHTML").ConfigureAwait(false);

    public async Task SetContentAsync(string html, NavigationOptions? options = null) =>
        await EvaluateAsync<object?>(
            $"document.open(); document.write({System.Text.Json.JsonSerializer.Serialize(html)}); document.close();").ConfigureAwait(false);

    public async Task<string> TitleAsync() =>
        await EvaluateAsync<string>("document.title").ConfigureAwait(false);

    public Task<IResponse?> GotoAsync(string url, NavigationOptions? options = null) =>
        IsMainFrame
            ? _page.GotoAsync(url, options)
            : _page.GotoInFrameAsync(Id, url, options);

    public Task WaitForLoadStateAsync(LoadState? state = null, double? timeout = null) =>
        _page.WaitForLoadStateAsync(state, timeout);

    public Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(urlPattern);
        var expected = _page.ResolveAgainstBaseUrl(urlPattern);
        return _page.WaitForFrameUrlAsync(
            this, url => Motus.Page.UrlMatchesWhole(url, expected), $"'{expected}'", options);
    }

    public Task WaitForURLAsync(System.Text.RegularExpressions.Regex urlPattern, NavigationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(urlPattern);
        return _page.WaitForFrameUrlAsync(this, urlPattern.IsMatch, $"/{urlPattern}/", options);
    }

    public Task WaitForURLAsync(Func<string, bool> predicate, NavigationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return _page.WaitForFrameUrlAsync(this, predicate, "a predicate", options);
    }

    // --- Locator methods ---

    public Task<AccessibilitySnapshot> AccessibilitySnapshotAsync(CancellationToken ct = default)
        => _page.AccessibilitySnapshotAsync(this, ct);

    public ILocator Locator(string selector, LocatorOptions? options = null)
        => new Locator(this, selector, options);

    public ILocator LocatorByBackendNodeId(long backendNodeId)
        => new Locator(this, $"{BackendNodeIdSelectorStrategy.Prefix}={backendNodeId}");

    public ILocator GetByRole(string role, string? name = null)
        => new Locator(this, GetBySelectors.Role(role, name));

    public ILocator GetByText(string text, bool? exact = null)
        => new Locator(this, GetBySelectors.Text(text, exact));

    public ILocator GetByLabel(string text, bool? exact = null)
        => new Locator(this, GetBySelectors.Label(text, exact));

    public ILocator GetByPlaceholder(string text, bool? exact = null)
        => new Locator(this, GetBySelectors.Placeholder(text, exact));

    public ILocator GetByTestId(string testId)
        => new Locator(this, GetBySelectors.TestId(testId));

    public ILocator GetByTitle(string text, bool? exact = null)
        => new Locator(this, GetBySelectors.Title(text, exact));

    public ILocator GetByAltText(string text, bool? exact = null)
        => new Locator(this, GetBySelectors.AltText(text, exact));

    public Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null)
        => _page.AddScriptTagAsync(url, content, this);

    public Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null)
        => _page.AddStyleTagAsync(url, content, this);
}
