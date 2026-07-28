using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Frames that render in their own process, and the sessions that reach them.
/// </summary>
internal sealed partial class Page
{
    /// <summary>
    /// The target type a frame hosted in its own renderer process reports itself as.
    /// </summary>
    private const string FrameTargetType = "iframe";

    /// <summary>
    /// The name given to the isolated world created per frame.
    /// </summary>
    private const string IsolatedWorldName = "__motus__";

    /// <summary>
    /// Asks a session to attach to the targets it hosts, so a frame that renders in its own process
    /// is reported rather than silently missing.
    /// </summary>
    /// <remarks>
    /// Called for the page session and again for every frame target adopted through it, because
    /// auto-attach covers one level: without re-arming on each new session, a frame inside a frame
    /// is never discovered, and two levels is the ordinary case rather than an exotic one.
    ///
    /// The new target is not held at a debugger break while it is set up. Pausing it is the only
    /// way to guarantee no event is missed, but <c>Runtime.enable</c> replays the contexts that
    /// already exist and the frame tree is read directly rather than waited for, which recovers the
    /// same state without a resume path that can strand a target if setup throws.
    /// </remarks>
    private static async Task ArmAutoAttachAsync(IMotusSession session, CancellationToken ct)
    {
        if ((session.Capabilities & MotusCapabilities.TargetMultiplexing) == 0)
            return;

        await session.SendAsync(
            "Target.setAutoAttach",
            new TargetSetAutoAttachParams(
                AutoAttach: true, WaitForDebuggerOnStart: false, Flatten: true),
            CdpJsonContext.Default.TargetSetAutoAttachParams,
            CdpJsonContext.Default.TargetSetAutoAttachResult,
            ct).ConfigureAwait(false);
    }

    private void OnTargetAttached(TargetAttachedToTargetEvent evt)
    {
        // Workers arrive here too and have no frame of their own. Nothing tracks them today, which
        // is the behavior this leaves alone.
        if (evt.TargetInfo.Type != FrameTargetType)
            return;

        // For a frame hosted out of process the target id is the frame id. That equality is what
        // makes the child entry the parent already reported and this separately attached target
        // recognizable as one frame rather than two.
        var frameId = evt.TargetInfo.TargetId;
        _frameTargetInit[frameId] = AdoptFrameTargetAsync(evt.SessionId, evt.TargetInfo);
    }

    /// <summary>
    /// Brings a frame that renders in its own process under this page: opens its session, starts
    /// listening to it, arms it for its own children, and stitches its subtree into the frame map.
    /// </summary>
    private async Task AdoptFrameTargetAsync(string sessionId, TargetInfo info)
    {
        var frameId = info.TargetId;
        var ct = _pageCts.Token;

        try
        {
            var session = _context.SessionRegistry.CreateSession(sessionId);

            // Recorded before the session is usable, so anything reaching the frame in the meantime
            // asks the right session and waits for it, rather than asking the page session and
            // getting a confidently wrong answer.
            _frameIdToSession[frameId] = session;

            // Subscribe before enabling, for the reason given in InitializeAsync: the browser fires
            // immediately on enable and a pump that is not yet listening drops those events.
            StartFrameStructureEventPump(session, ct);

            await session.SendAsync("Page.enable",
                CdpJsonContext.Default.PageEnableResult, ct).ConfigureAwait(false);
            await session.SendAsync("Runtime.enable",
                CdpJsonContext.Default.RuntimeEnableResult, ct).ConfigureAwait(false);

            await ArmAutoAttachAsync(session, ct).ConfigureAwait(false);

            // A target that has already navigated replays nothing on enable, so its tree is read
            // rather than waited for. This is also what fills in the frame's own entry, with the
            // parent the child target reports for itself.
            var tree = await session.SendAsync(
                "Page.getFrameTree",
                CdpJsonContext.Default.PageGetFrameTreeResult,
                ct).ConfigureAwait(false);

            RecordFrameTreeNode(tree.FrameTree, session);

            if (_frames.TryGetValue(frameId, out var frame))
                FrameAttached?.Invoke(this, frame);
        }
        catch (OperationCanceledException)
        {
            // The page is going away.
        }
        catch (Exception ex)
        {
            // One frame that will not answer is not a reason to fault the page it sits in. The
            // routing is dropped so the frame falls back to whatever the parent knows about it.
            _frameIdToSession.TryRemove(frameId, out _);
            System.Console.Error.WriteLine($"Motus: a frame target could not be adopted ({ex.Message}).");
        }
    }

    private void OnTargetDetached(TargetDetachedFromTargetEvent evt)
    {
        foreach (var (frameId, session) in _frameIdToSession)
        {
            if (session.SessionId != evt.SessionId)
                continue;

            // Losing the session means the frame is no longer hosted out of process, which is not
            // the same as the frame being gone: a cross-origin frame navigating back to its
            // parent's origin gives up its target and keeps existing. Dropping the routing returns
            // it to the page session, and Page.frameDetached from the parent stays the only signal
            // that a frame has actually gone away.
            _frameIdToSession.TryRemove(frameId, out _);
            _frameIdToExecutionContext.TryRemove(frameId, out _);
            _frameIdToIsolatedWorld.TryRemove(frameId, out _);
            _frameTargetInit.TryRemove(frameId, out _);
        }

        if (_context.SessionRegistry.TryGetSession(evt.SessionId, out var detached))
            detached?.CleanupChannels();

        _context.SessionRegistry.RemoveSession(evt.SessionId);
    }

    /// <summary>
    /// Drops every session this page opened for a frame of its own.
    /// </summary>
    /// <remarks>
    /// The page's own session is released by the context that owns the page. The sessions opened
    /// here are not, and a browser driven for a long time across many pages would otherwise
    /// accumulate one registry entry and one set of event channels per frame it ever saw.
    /// </remarks>
    private void ReleaseFrameSessions()
    {
        foreach (var (frameId, session) in _frameIdToSession)
        {
            _frameIdToSession.TryRemove(frameId, out _);

            if (session.SessionId is not { } sessionId)
                continue;

            session.CleanupChannels();
            _context.SessionRegistry.RemoveSession(sessionId);
        }
    }

    /// <summary>
    /// Whether the given frame is reached over a session of its own rather than the page's, which
    /// is true exactly when the browser has put it in its own process.
    /// </summary>
    internal bool HasOwnSession(IFrame frame) =>
        frame is Frame f && _frameIdToSession.ContainsKey(f.Id);

    /// <summary>
    /// Completes once the given frame is ready to be talked to.
    /// </summary>
    /// <remarks>
    /// Adopting a frame target is driven by an event, so a caller can hold a frame before its
    /// session has finished initializing. Without this, a locator against a frame that has just
    /// appeared fails for being early rather than for being wrong.
    /// </remarks>
    internal Task WhenFrameReadyAsync(string frameId) =>
        _frameTargetInit.TryGetValue(frameId, out var init) ? init : Task.CompletedTask;

    internal Task WhenFrameReadyAsync(IFrame? frame) =>
        frame is Frame f ? WhenFrameReadyAsync(f.Id) : Task.CompletedTask;

    /// <summary>
    /// Returns where the given frame's own coordinate space begins on the page.
    /// </summary>
    /// <remarks>
    /// A renderer measures against its own local root. For everything one renderer hosts, that root
    /// is the page and the answer is zero. A frame with a process of its own is its own root and
    /// knows nothing about where it was embedded, so its origin is the position of the element that
    /// hosts it, read from the parent, accumulated across every process boundary up to the page.
    /// </remarks>
    internal async Task<(double X, double Y)> GetFrameOriginAsync(IFrame? frame, CancellationToken ct)
    {
        if (frame is not Frame current || !_frameIdToSession.ContainsKey(current.Id))
            return (0, 0);

        double x = 0, y = 0;

        while (current.ParentFrameId is not null
               && _frames.TryGetValue(current.ParentFrameId, out var parent))
        {
            var hostSession = SessionFor(parent);

            // Same renderer on both sides means the child was already measured against this root.
            if (!ReferenceEquals(hostSession, SessionFor(current)))
            {
                var origin = await GetFrameOwnerOriginAsync(hostSession, current.Id, ct).ConfigureAwait(false);
                if (origin is null)
                    break;

                x += origin.Value.X;
                y += origin.Value.Y;
            }

            current = parent;
        }

        return (x, y);
    }

    /// <summary>
    /// Reads the content-box origin of the element hosting the given frame, measured on the session
    /// that owns that element.
    /// </summary>
    private async Task<(double X, double Y)?> GetFrameOwnerOriginAsync(
        IMotusSession session, string frameId, CancellationToken ct)
    {
        try
        {
            var owner = await session.SendAsync(
                "DOM.getFrameOwner",
                new DomGetFrameOwnerParams(frameId),
                CdpJsonContext.Default.DomGetFrameOwnerParams,
                CdpJsonContext.Default.DomGetFrameOwnerResult,
                ct).ConfigureAwait(false);

            var model = await session.SendAsync(
                "DOM.getBoxModel",
                new DomGetBoxModelParams(BackendNodeId: owner.BackendNodeId),
                CdpJsonContext.Default.DomGetBoxModelParams,
                CdpJsonContext.Default.DomGetBoxModelResult,
                ct).ConfigureAwait(false);

            // The frame's viewport starts at the host element's content box, inside its border and
            // padding, which is where the frame's own origin lands.
            return model.Model.Content.Length < 2
                ? null
                : (model.Model.Content[0], model.Model.Content[1]);
        }
        catch (MotusProtocolException)
        {
            // The frame or its host is already gone.
            return null;
        }
    }

    /// <summary>
    /// Returns the execution context of the frame's isolated world, creating it on first ask.
    /// </summary>
    /// <remarks>
    /// The world belongs to the frame's current document, so it is dropped when the frame navigates
    /// and the next ask makes a new one.
    /// </remarks>
    internal async Task<int?> GetIsolatedWorldContextIdAsync(string frameId)
    {
        // Lazy rather than a bare task, because GetOrAdd may run its factory more than once under
        // contention and only keep one result. With a task that would mean a second world created
        // in the renderer and never referred to again.
        var pending = _frameIdToIsolatedWorld.GetOrAdd(
            frameId,
            id => new Lazy<Task<int>>(() => CreateIsolatedWorldAsync(id)));

        try
        {
            return await pending.Value.ConfigureAwait(false);
        }
        catch
        {
            // A failed creation must not be cached, or the frame can never get a world again.
            _frameIdToIsolatedWorld.TryRemove(new KeyValuePair<string, Lazy<Task<int>>>(frameId, pending));
            throw;
        }
    }

    private async Task<int> CreateIsolatedWorldAsync(string frameId)
    {
        _frames.TryGetValue(frameId, out var frame);

        var result = await SessionFor(frame).SendAsync(
            "Page.createIsolatedWorld",
            new PageCreateIsolatedWorldParams(
                FrameId: frameId,
                WorldName: IsolatedWorldName,
                GrantUniveralAccess: true),
            CdpJsonContext.Default.PageCreateIsolatedWorldParams,
            CdpJsonContext.Default.PageCreateIsolatedWorldResult,
            _pageCts.Token).ConfigureAwait(false);

        return result.ExecutionContextId;
    }
}
