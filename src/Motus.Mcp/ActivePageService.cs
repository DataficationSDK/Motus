using System.Runtime.CompilerServices;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Resolves the page that unscoped tool calls act on and keeps the per-page
/// snapshot service alive between calls. Tool invocations arrive as individually
/// stateless messages, so the ref map a <c>snapshot</c> produced has to survive
/// here to be usable by the <c>click</c>/<c>type</c> calls that follow it.
/// </summary>
/// <remarks>
/// <see cref="BrowserSessionManager"/> owns the browser and its contexts; this
/// service owns the page layer on top. It caches one active page and, for each
/// page it has seen, one <see cref="PageSnapshotService"/>. The cache uses a
/// <see cref="ConditionalWeakTable{TKey,TValue}"/> so a page that is closed and
/// collected takes its snapshot service with it.
/// </remarks>
public class ActivePageService
{
    private readonly BrowserSessionManager _sessions;
    private readonly DialogService? _dialogService;
    private readonly ConsoleService? _consoleService;
    private readonly NetworkService? _networkService;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConditionalWeakTable<IPage, PageSnapshotService> _snapshots = new();

    private IPage? _activePage;
    private int _disposed;

    public ActivePageService(
        BrowserSessionManager sessions,
        DialogService? dialogService = null,
        ConsoleService? consoleService = null,
        NetworkService? networkService = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
        _dialogService = dialogService;
        _consoleService = consoleService;
        _networkService = networkService;
    }

    /// <summary>
    /// Returns the active page, reusing the cached one while it is still open and
    /// otherwise resolving a fresh one. The browser and its active context are
    /// launched lazily through <see cref="BrowserSessionManager"/>.
    /// </summary>
    public async Task<IPage> GetOrCreateActivePageAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activePage is { IsClosed: false })
                return _activePage;

            _activePage = await ResolvePageAsync(cancellationToken).ConfigureAwait(false);
            SubscribeObservers(_activePage);
            return _activePage;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Resolves the page to treat as active: the first open page of the active
    /// context, or a new page when the context has none. This is the only step
    /// that touches the browser, so tests override it to supply a fake page.
    /// </summary>
    protected virtual async Task<IPage> ResolvePageAsync(CancellationToken cancellationToken)
    {
        var context = await _sessions.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);

        foreach (var page in context.Pages)
        {
            if (!page.IsClosed)
                return page;
        }

        return await context.NewPageAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the snapshot service for a page, creating it on first request. The
    /// same instance is returned for the same page, so refs taken in one call
    /// resolve in the next.
    /// </summary>
    public PageSnapshotService GetSnapshotService(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return _snapshots.GetValue(page, static p => new PageSnapshotService(p));
    }

    /// <summary>
    /// Drops a page's snapshot service so the refs from its last snapshot no longer
    /// resolve. Called after a navigation, which invalidates those refs; a
    /// subsequent ref-addressed call then reports that a fresh snapshot is needed.
    /// </summary>
    public void InvalidateSnapshot(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _snapshots.Remove(page);
    }

    /// <summary>
    /// Makes a specific page the active one, so the calls that follow act on it.
    /// Used when opening or switching tabs.
    /// </summary>
    public void SelectPage(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        // Not gated: a tab switch is followed by ordinary tool calls, which the
        // protocol delivers one at a time, so there is no concurrent reader of the
        // active page to race against here.
        _activePage = page;
        SubscribeObservers(page);
    }

    /// <summary>
    /// Points the page-following observers (dialog, console, network log) at the
    /// given page, so each captures the active tab's events. Called whenever the
    /// active page is resolved or switched.
    /// </summary>
    private void SubscribeObservers(IPage page)
    {
        _dialogService?.Subscribe(page);
        _consoleService?.Subscribe(page);
        _networkService?.SubscribePage(page);
    }

    /// <summary>
    /// Forgets the cached active page so the next request resolves a fresh one.
    /// Used after closing a tab or switching context, where the previously active
    /// page may no longer belong to the active context.
    /// </summary>
    public void ResetActivePage() => _activePage = null;

    /// <summary>The names of the currently open contexts.</summary>
    public virtual IReadOnlyCollection<string> GetContextNames() => _sessions.ContextNames;

    /// <summary>The name of the context that unscoped tool calls act on.</summary>
    public virtual string GetActiveContextName() => _sessions.ActiveContextName;

    /// <summary>
    /// Returns the active context, launching the browser and creating the context on
    /// first use. This touches the browser, so tests override it to supply a fake
    /// context. Used by the network tools to register context-level route rules.
    /// </summary>
    public virtual Task<IBrowserContext> GetOrCreateActiveContextAsync(CancellationToken cancellationToken = default)
        => _sessions.GetOrCreateActiveContextAsync(cancellationToken);

    /// <summary>
    /// Returns the open pages of the active context. This touches the browser, so
    /// tests override it to supply fake pages.
    /// </summary>
    protected virtual async Task<IReadOnlyList<IPage>> GetActiveContextPagesAsync(CancellationToken cancellationToken)
    {
        var context = await _sessions.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
        return context.Pages.Where(p => !p.IsClosed).ToArray();
    }

    /// <summary>Lists the active context's open tabs, in order.</summary>
    public Task<IReadOnlyList<IPage>> ListTabsAsync(CancellationToken cancellationToken = default)
        => GetActiveContextPagesAsync(cancellationToken);

    /// <summary>
    /// Opens a new tab in the active context and makes it active. This touches the
    /// browser, so tests override it.
    /// </summary>
    public virtual async Task<IPage> OpenNewTabAsync(CancellationToken cancellationToken = default)
    {
        var context = await _sessions.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);
        var page = await context.NewPageAsync().ConfigureAwait(false);
        SelectPage(page);
        return page;
    }

    /// <summary>
    /// Makes the tab at the given zero-based index active and brings it to the
    /// foreground. The index runs over the active context's open tabs.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the open-tab range.</exception>
    public async Task<IPage> SelectTabAsync(int index, CancellationToken cancellationToken = default)
    {
        var pages = await GetActiveContextPagesAsync(cancellationToken).ConfigureAwait(false);
        if (index < 0 || index >= pages.Count)
            throw new IndexOutOfRangeException(
                $"Tab index {index} is out of range; {pages.Count} tab(s) are open. Use tab_list to see them.");

        var page = pages[index];
        await page.BringToFrontAsync().ConfigureAwait(false);
        SelectPage(page);
        return page;
    }

    /// <summary>
    /// Closes the tab at the given index, or the active tab when no index is given,
    /// then forgets the active page so the next request resolves a remaining tab.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the open-tab range.</exception>
    public async Task<int> CloseTabAsync(int? index, CancellationToken cancellationToken = default)
    {
        var pages = await GetActiveContextPagesAsync(cancellationToken).ConfigureAwait(false);

        int target;
        if (index is { } i)
        {
            if (i < 0 || i >= pages.Count)
                throw new IndexOutOfRangeException(
                    $"Tab index {i} is out of range; {pages.Count} tab(s) are open. Use tab_list to see them.");
            target = i;
        }
        else
        {
            var active = await GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            target = IndexOf(pages, active);
            if (target < 0)
                target = 0;
        }

        ResetActivePage();
        await pages[target].CloseAsync().ConfigureAwait(false);
        return target;

        static int IndexOf(IReadOnlyList<IPage> list, IPage page)
        {
            for (var n = 0; n < list.Count; n++)
            {
                if (ReferenceEquals(list[n], page))
                    return n;
            }

            return -1;
        }
    }

    /// <summary>
    /// Creates a new isolated context, makes it active, and forgets the active page
    /// so the next request resolves one from the new context. Touches the browser,
    /// so tests override it.
    /// </summary>
    public virtual async Task CreateContextAsync(string name, CancellationToken cancellationToken = default)
    {
        await _sessions.CreateContextAsync(name, cancellationToken).ConfigureAwait(false);
        ResetActivePage();
    }

    /// <summary>
    /// Makes an existing context active and forgets the active page. Touches the
    /// session state only, so it is virtual for the same test reason.
    /// </summary>
    public virtual void SelectContext(string name)
    {
        _sessions.SelectContext(name);
        ResetActivePage();
    }

    /// <summary>
    /// Closes the named context and forgets the active page. Touches the browser, so
    /// tests override it.
    /// </summary>
    public virtual async Task CloseContextAsync(string name, CancellationToken cancellationToken = default)
    {
        await _sessions.CloseContextAsync(name, cancellationToken).ConfigureAwait(false);
        ResetActivePage();
    }

    /// <summary>
    /// Releases this service's own resources. Pages and contexts are owned by
    /// <see cref="BrowserSessionManager"/> and torn down there, so this only drops the
    /// active-page reference and the gate.
    /// </summary>
    /// <remarks>
    /// This is deliberately not <see cref="IAsyncDisposable"/>. In the HTTP host the
    /// service is handed to tools through a per-session factory, and an
    /// <c>IAsyncDisposable</c> resolved that way would be disposed by the DI container at
    /// the end of every tool call, tearing down state the session still needs. Lifetime is
    /// owned by the session bundle, which calls this once when the session ends.
    /// </remarks>
    internal void Shutdown()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _activePage = null;
            _gate.Dispose();
        }
    }
}
