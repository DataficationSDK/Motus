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
public class ActivePageService : IAsyncDisposable
{
    private readonly BrowserSessionManager _sessions;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConditionalWeakTable<IPage, PageSnapshotService> _snapshots = new();

    private IPage? _activePage;
    private int _disposed;

    public ActivePageService(BrowserSessionManager sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
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
    /// Releases this service's own resources. Pages and contexts are owned by
    /// <see cref="BrowserSessionManager"/> and torn down there.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _activePage = null;
            _gate.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}
