using System.Runtime.CompilerServices;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// One frame of the active page, and how deeply it is nested inside it. Depth is carried because
/// the flat list is the addressing scheme and the nesting is otherwise invisible in it.
/// </summary>
public sealed record FrameEntry(IFrame Frame, int Depth);

/// <summary>
/// One open tab and the name of the context holding it. The context is carried because the tab
/// index runs across every context the session holds, so selecting a tab can mean moving to the
/// context it lives in.
/// </summary>
public sealed record TabEntry(IPage Page, string ContextName);

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
    private IFrame? _activeFrame;
    private int _activePageGeneration;
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

        HasCoordinateTools = ToolCapabilities.Includes(sessions.Options.Capabilities, ToolCapabilities.Coordinates);
    }

    /// <summary>
    /// The dialog watcher following the active page, or null when the session has none. Exposed so
    /// a tool can race its action against a dialog without taking a second injected service: the
    /// dialog follows the active page, and this is what owns that.
    /// </summary>
    public DialogService? Dialogs => _dialogService;

    /// <summary>
    /// The console capture following the active page, or null when the session has none. Exposed
    /// for the same reason as <see cref="Dialogs"/>: what an action says about the errors it
    /// caused is read from the capture that follows the page it ran against.
    /// </summary>
    public ConsoleService? ConsoleLog => _consoleService;

    /// <summary>
    /// How long an element action may take, in milliseconds, or null for the framework default.
    /// Neither a page nor a context holds a default for this, so every tool passes it on the call
    /// it makes, and reads it from here so there is one place it comes from.
    /// </summary>
    public double? ActionTimeout => _sessions.Options.ActionTimeout;

    /// <summary>
    /// Whether the tools that act on a position are in this server's catalog.
    /// </summary>
    /// <remarks>
    /// Read once at construction, because what a result tells an agent to do next turns on it: a
    /// page nothing can address is a dead end when there is no way to click a coordinate, and a
    /// recovery it can follow when there is. A message that describes both cases and asks the agent
    /// to work out which one it is in gets answered inconsistently.
    /// </remarks>
    public bool HasCoordinateTools { get; }

    /// <summary>
    /// The options every navigation is made with, carrying the configured navigation timeout, or
    /// null when none was configured and the framework default applies.
    /// </summary>
    public NavigationOptions? Navigation => _sessions.Options.NavigationTimeout is { } timeout
        ? new NavigationOptions { Timeout = timeout }
        : null;

    /// <summary>
    /// How long an action result waits for the page to show what the action did before it is
    /// written: the configured settle time, or the default when none was given.
    /// </summary>
    public TimeSpan Settle => TimeSpan.FromMilliseconds(
        _sessions.Options.SettleTimeout ?? McpServerLaunchOptions.DefaultSettleMilliseconds);

    /// <summary>
    /// Returns the active page, reusing the cached one while it is still open and
    /// belongs to the current browser, and otherwise resolving a fresh one. The
    /// browser and its active context are launched lazily through
    /// <see cref="BrowserSessionManager"/>.
    /// </summary>
    /// <remarks>
    /// A page whose browser has crashed keeps reporting <see cref="IPage.IsClosed"/> as false
    /// (nothing disposes it), so the open check alone is not enough to drop it. Two further
    /// guards handle a crash: the current browser must not be dead (catches a crash not yet
    /// relaunched, since the relaunch only happens while re-resolving), and the page's browser
    /// generation must still be current (catches a page left over from before a relaunch that a
    /// context-level call already performed). When either fails, a fresh page is resolved against
    /// the live browser.
    /// </remarks>
    public async Task<IPage> GetOrCreateActivePageAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_activePage is { IsClosed: false }
                && !_sessions.IsBrowserDead
                && _activePageGeneration == _sessions.Generation)
                return _activePage;

            _activePage = await ResolvePageAsync(cancellationToken).ConfigureAwait(false);
            _activePageGeneration = _sessions.Generation;
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
        return _snapshots.GetValue(page, p => new PageSnapshotService(p, HasCoordinateTools));
    }

    /// <summary>
    /// Whether a snapshot of this page has been taken and its refs are still being held. Asked
    /// before an action so the result can say when the action has just made those refs point at a
    /// document that is no longer there.
    /// </summary>
    /// <remarks>
    /// This does not create a snapshot service the way <see cref="GetSnapshotService"/> does, so
    /// asking the question never changes the answer.
    /// </remarks>
    public bool HasSnapshot(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return _snapshots.TryGetValue(page, out var service) && service.LastSnapshot is not null;
    }

    /// <summary>
    /// The frames the page's last snapshot printed inside itself, with the address each held at the
    /// time, or an empty list when there is no snapshot to speak of.
    /// </summary>
    /// <remarks>
    /// Asked before an action so the result can say when a frame has gone somewhere else under the
    /// refs the agent is holding. Like <see cref="HasSnapshot"/>, this does not create a snapshot
    /// service, so asking never changes the answer.
    /// </remarks>
    public IReadOnlyList<SnapshotFrame> SnapshotFrames(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return _snapshots.TryGetValue(page, out var service) ? service.InlinedFrames : [];
    }

    /// <summary>
    /// The frame the page's last snapshot described on its own, with the address it held at the
    /// time, or null when that snapshot described the page or there is none.
    /// </summary>
    /// <remarks>
    /// A snapshot of one frame prints no frames inside itself, so <see cref="SnapshotFrames"/> is
    /// empty for it and the one document its refs came from would otherwise go unwatched. Like
    /// <see cref="HasSnapshot"/>, this does not create a snapshot service.
    /// </remarks>
    public SnapshotScope? SnapshotScope(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return _snapshots.TryGetValue(page, out var service) ? service.ScopedTo : null;
    }

    /// <summary>
    /// Drops a page's snapshot service so the refs from its last snapshot no longer
    /// resolve. Called after a navigation, which invalidates those refs; a
    /// subsequent ref-addressed call then reports that a fresh snapshot is needed.
    /// </summary>
    /// <remarks>
    /// The frame selection is dropped here too. Every tool that navigates or switches tabs already
    /// calls this and none of them leave the selected frame meaningful, while <c>snapshot</c> does
    /// not call it, which is exactly the split that would be tedious to maintain by hand at each
    /// call site.
    /// </remarks>
    public void InvalidateSnapshot(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _snapshots.Remove(page);
        _activeFrame = null;
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
        _activeFrame = null;
        _activePageGeneration = _sessions.Generation;
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
    public void ResetActivePage()
    {
        _activePage = null;
        _activeFrame = null;
    }

    /// <summary>
    /// Lists the frames of the active page, the main frame first and every other frame after the
    /// one that holds it, each with how deeply it is nested. The index of each entry is what
    /// <see cref="SelectFrameAsync"/> takes.
    /// </summary>
    /// <remarks>
    /// The tree is walked from the main frame rather than read from <see cref="IPage.Frames"/>, so
    /// a frame always follows its parent rather than landing wherever it happened to be discovered.
    /// A frame that arrives late, which is the ordinary case for one the browser puts in its own
    /// process, would otherwise turn up in an arbitrary place in the list. Frames that share a
    /// parent come in the order they attached, which is the order their elements appear in the
    /// document unless the page inserted one later.
    /// </remarks>
    public virtual async Task<IReadOnlyList<FrameEntry>> ListFramesAsync(CancellationToken cancellationToken = default)
    {
        var page = await GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
        var entries = new List<FrameEntry>();
        Collect(page.MainFrame, depth: 0, entries);
        return entries;

        static void Collect(IFrame frame, int depth, List<FrameEntry> into)
        {
            into.Add(new FrameEntry(frame, depth));
            foreach (var child in frame.ChildFrames)
                Collect(child, depth + 1, into);
        }
    }

    /// <summary>
    /// Makes the frame at the given index the one that perception and evaluation act on. Index 0 is
    /// the main frame and returns the session to page scope.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the frame range.</exception>
    public async Task<IFrame> SelectFrameAsync(int index, CancellationToken cancellationToken = default)
    {
        var frames = await ListFramesAsync(cancellationToken).ConfigureAwait(false);
        if (index < 0 || index >= frames.Count)
            throw new IndexOutOfRangeException(
                $"Frame index {index} is out of range; the page has {frames.Count} frame(s). "
                + "Use frame_list to see them.");

        var frame = frames[index].Frame;

        // The main frame is page scope, so selecting it clears the scope rather than recording it.
        // Nothing downstream then has to treat "the main frame" as a special case of a frame.
        _activeFrame = index == 0 ? null : frame;
        return frame;
    }

    /// <summary>
    /// Returns the frame the session is scoped to, or null when it is scoped to the page.
    /// </summary>
    /// <remarks>
    /// A frame that has since been removed from its page is dropped here rather than handed back.
    /// Otherwise the failure surfaces as an unrelated protocol error on whatever call happened to
    /// use it next.
    /// </remarks>
    public IFrame? GetActiveFrame()
    {
        if (_activeFrame is { IsDetached: true })
            _activeFrame = null;

        return _activeFrame;
    }

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
    /// Returns the open pages of every context this session holds, context by context and then in
    /// the order each context holds its pages. This touches the browser, so tests override it to
    /// supply fake tabs.
    /// </summary>
    /// <remarks>
    /// One list across every context is what makes a tab addressable wherever it is. A tab in
    /// another context used to be reachable only by naming its context first, and a tab adopted
    /// from an attached browser into a context the session did not create was counted but never
    /// listed. The active context is resolved first so a session that has not touched the browser
    /// yet has something to list.
    /// </remarks>
    protected virtual async Task<IReadOnlyList<TabEntry>> GetOpenTabsAsync(CancellationToken cancellationToken)
    {
        await _sessions.GetOrCreateActiveContextAsync(cancellationToken).ConfigureAwait(false);

        var tabs = new List<TabEntry>();
        foreach (var (name, context) in _sessions.HeldContexts)
        {
            foreach (var page in context.Pages)
            {
                if (!page.IsClosed)
                    tabs.Add(new TabEntry(page, name));
            }
        }

        return tabs;
    }

    /// <summary>Lists the open tabs of every context this session holds, in order.</summary>
    public async Task<IReadOnlyList<TabEntry>> ListTabsAsync(CancellationToken cancellationToken = default)
        => await GetOpenTabsAsync(cancellationToken).ConfigureAwait(false);

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
    /// Makes the tab at the given zero-based index active and brings it to the foreground. The
    /// index runs over every context's open tabs, and a tab in another context moves the session to
    /// that context as well.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the open-tab range.</exception>
    public async Task<IPage> SelectTabAsync(int index, CancellationToken cancellationToken = default)
    {
        var tabs = await GetOpenTabsAsync(cancellationToken).ConfigureAwait(false);
        if (index < 0 || index >= tabs.Count)
            throw new IndexOutOfRangeException(
                $"Tab index {index} is out of range; {tabs.Count} tab(s) are open. Use tab_list to see them.");

        var tab = tabs[index];

        // Naming a tab is enough to say where the session should be working. Leaving the active
        // context behind would make every call that followed act on a page in a different context
        // from the one just selected.
        if (!string.Equals(tab.ContextName, GetActiveContextName(), StringComparison.Ordinal))
            SelectContext(tab.ContextName);

        await tab.Page.BringToFrontAsync().ConfigureAwait(false);
        SelectPage(tab.Page);
        return tab.Page;
    }

    /// <summary>
    /// Closes the tab at the given index, or the active tab when no index is given,
    /// then forgets the active page so the next request resolves a remaining tab.
    /// </summary>
    /// <exception cref="IndexOutOfRangeException">The index is outside the open-tab range.</exception>
    public async Task<int> CloseTabAsync(int? index, CancellationToken cancellationToken = default)
    {
        var tabs = await GetOpenTabsAsync(cancellationToken).ConfigureAwait(false);

        int target;
        if (index is { } i)
        {
            if (i < 0 || i >= tabs.Count)
                throw new IndexOutOfRangeException(
                    $"Tab index {i} is out of range; {tabs.Count} tab(s) are open. Use tab_list to see them.");
            target = i;
        }
        else
        {
            var active = await GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            target = IndexOf(tabs, active);
            if (target < 0)
                target = 0;
        }

        ResetActivePage();
        await tabs[target].Page.CloseAsync().ConfigureAwait(false);
        return target;

        static int IndexOf(IReadOnlyList<TabEntry> list, IPage page)
        {
            for (var n = 0; n < list.Count; n++)
            {
                if (ReferenceEquals(list[n].Page, page))
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
    /// Points the session at a browser that is already running and forgets the active page, so the
    /// next request resolves one from what that browser already has open. Touches the browser, so
    /// tests override it.
    /// </summary>
    public virtual async Task AttachAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        await _sessions.AttachAsync(endpoint, cancellationToken).ConfigureAwait(false);
        ResetActivePage();
    }

    /// <summary>Whether the live browser is one this session connected to rather than started.</summary>
    public virtual bool IsAttached => _sessions.IsAttached;

    /// <summary>The endpoint this session attaches to, or null when it starts its own browser.</summary>
    public virtual string? Endpoint => _sessions.Endpoint;

    /// <summary>Whether a browser has been acquired yet.</summary>
    public virtual bool IsBrowserLaunched => _sessions.IsBrowserLaunched;

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
