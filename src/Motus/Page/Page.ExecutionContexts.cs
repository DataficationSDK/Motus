using System.Collections.Concurrent;
using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// The execution context each frame's main world currently runs in, and what a caller does when a
/// frame has none yet.
/// </summary>
internal sealed partial class Page
{
    /// <summary>
    /// How long a command waits for a frame to report its next main world when the caller gives no
    /// shorter bound of its own. The same default every other wait in Motus uses.
    /// </summary>
    private static readonly TimeSpan ExecutionContextWaitTimeout = TimeSpan.FromMilliseconds(30_000);

    /// <summary>
    /// The text the browser answers with when a command names an execution context that no longer
    /// exists.
    /// </summary>
    private const string StaleContextMessage = "Cannot find context with specified id";

    /// <summary>
    /// A frame's main world: its context id, and the session that reported it.
    /// </summary>
    /// <remarks>
    /// A context id is numbered by the renderer that owns it, so two sessions can report the same
    /// number for two different worlds, and an id means nothing on any session but its own. Keeping
    /// the session alongside the id is what lets a late notice from one session leave alone a
    /// context another session has since reported for the same frame.
    /// </remarks>
    private readonly record struct FrameExecutionContext(int Id, IMotusSession Session);

    /// <summary>
    /// Where a command about a frame is sent: the session, and the context to name, or null for the
    /// session's default, which is the main world of the frame at that session's root.
    /// </summary>
    internal readonly record struct ExecutionTarget(IMotusSession Session, int? ContextId);

    private readonly ConcurrentDictionary<string, FrameExecutionContext> _frameIdToExecutionContext = new();

    // Callers waiting for a frame to report its next main world. One completion per frame, shared by
    // every caller waiting on that frame and replaced once it fires, so it says only that something
    // changed and each caller looks again. It fires when a context is recorded for the frame and when
    // the frame goes away, so nobody waits out the clock on a frame that will never answer.
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _executionContextWaiters = new();

    /// <summary>
    /// Reads the events that make and unmake execution contexts on one session, in the order the
    /// browser sent them.
    /// </summary>
    /// <remarks>
    /// A navigation tears down the old document's contexts and announces the new document's in that
    /// order. Read on separate loops, the new document's context could be recorded first and the
    /// teardown handled after it, which would wipe out the one context the frame actually has. One
    /// channel for the three keeps the teardown ahead of the replacement.
    /// <para>
    /// WebDriver BiDi has no counterpart to subscribe to here. Script there is addressed to a
    /// browsing context and runs in whatever realm that context holds at the time, so there is no id
    /// to go stale and nothing to track.
    /// </para>
    /// </remarks>
    private async Task PumpExecutionContextEventsAsync(IMotusSession session, CancellationToken ct)
    {
        string[] events =
        [
            "Runtime.executionContextCreated",
            "Runtime.executionContextDestroyed",
            "Runtime.executionContextsCleared",
        ];

        try
        {
            await foreach (var raw in session.SubscribeAsync(events, ct).ConfigureAwait(false))
            {
                try
                {
                    switch (raw.Method)
                    {
                        case "Runtime.executionContextCreated":
                            if (JsonSerializer.Deserialize(raw.Params, CdpJsonContext.Default.RuntimeExecutionContextCreatedEvent) is { } created)
                                OnExecutionContextCreated(created, session);
                            break;
                        case "Runtime.executionContextDestroyed":
                            if (JsonSerializer.Deserialize(raw.Params, CdpJsonContext.Default.RuntimeExecutionContextDestroyedEvent) is { } destroyed)
                                OnExecutionContextDestroyed(destroyed.ExecutionContextId, session);
                            break;
                        case "Runtime.executionContextsCleared":
                            OnExecutionContextsCleared(session);
                            break;
                    }
                }
                catch
                {
                    // Prevent a malformed event from killing the event pump
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on page close
        }
    }

    private void OnExecutionContextCreated(RuntimeExecutionContextCreatedEvent evt, IMotusSession source)
    {
        var ctx = evt.Context;

        // Extract frameId from auxData if present
        string? frameId = null;
        var isDefaultWorld = true;
        if (ctx.AuxData is JsonElement aux && aux.ValueKind == JsonValueKind.Object)
        {
            if (aux.TryGetProperty("frameId", out var fid))
                frameId = fid.GetString();

            // A frame has one main world and any number of others beside it. Only the main world
            // is the frame's context: an isolated world reports the same frame id, and recording
            // it here would send every later main-world evaluation into the isolated world, where
            // the page's own globals do not exist. That failure is silent, so it is guarded at the
            // one place contexts are recorded rather than at each reader.
            if (aux.TryGetProperty("isDefault", out var isDefault)
                && isDefault.ValueKind is JsonValueKind.False)
            {
                isDefaultWorld = false;
            }
        }

        if (frameId is null || !isDefaultWorld)
            return;

        _frameIdToExecutionContext[frameId] = new FrameExecutionContext(ctx.Id, source);
        SignalExecutionContextWaiters(frameId);
    }

    /// <summary>
    /// Forgets a context the browser has torn down, wherever it was recorded.
    /// </summary>
    /// <remarks>
    /// Only an entry with the same id from the same session is dropped. A frame that has moved to
    /// another process already has its new context recorded from the new session by the time the
    /// old session reports the old one gone, and that report is not about the new one.
    /// </remarks>
    private void OnExecutionContextDestroyed(int contextId, IMotusSession source)
    {
        foreach (var (frameId, context) in _frameIdToExecutionContext)
        {
            if (context.Id == contextId && ReferenceEquals(context.Session, source))
                _frameIdToExecutionContext.TryRemove(new KeyValuePair<string, FrameExecutionContext>(frameId, context));
        }

        foreach (var frameId in _frameIdToIsolatedWorld.Keys)
        {
            _frames.TryGetValue(frameId, out var frame);
            if (ReferenceEquals(SessionFor(frame), source))
                DropIsolatedWorld(frameId, contextId);
        }
    }

    /// <summary>
    /// Forgets every context one session reported, which is what the browser announces when the
    /// document at that session's root is replaced.
    /// </summary>
    private void OnExecutionContextsCleared(IMotusSession source)
    {
        foreach (var (frameId, context) in _frameIdToExecutionContext)
        {
            if (ReferenceEquals(context.Session, source))
                _frameIdToExecutionContext.TryRemove(new KeyValuePair<string, FrameExecutionContext>(frameId, context));
        }

        foreach (var (frameId, world) in _frameIdToIsolatedWorld)
        {
            _frames.TryGetValue(frameId, out var frame);
            if (ReferenceEquals(SessionFor(frame), source))
                _frameIdToIsolatedWorld.TryRemove(new KeyValuePair<string, Lazy<Task<int>>>(frameId, world));
        }
    }

    /// <summary>
    /// Forgets every context reported over a session that has gone away.
    /// </summary>
    /// <remarks>
    /// Only contexts that session reported are dropped, not every context of the frames it hosted.
    /// A frame moving back into its parent's process has its new context reported by the parent's
    /// session, and that can be recorded before the old target's departure is handled. Dropping it
    /// then would leave the frame waiting for a context that has already arrived.
    /// </remarks>
    private void ForgetExecutionContextsFrom(string? sessionId)
    {
        if (sessionId is null)
            return;

        foreach (var (frameId, context) in _frameIdToExecutionContext)
        {
            if (context.Session.SessionId == sessionId)
                _frameIdToExecutionContext.TryRemove(new KeyValuePair<string, FrameExecutionContext>(frameId, context));
        }
    }

    /// <summary>
    /// Drops the frame's isolated world when it is the one with the given id, so the next ask makes
    /// a fresh one rather than handing back a world the browser has torn down.
    /// </summary>
    private void DropIsolatedWorld(string frameId, int contextId)
    {
        if (_frameIdToIsolatedWorld.TryGetValue(frameId, out var world)
            && world.IsValueCreated
            && world.Value.IsCompletedSuccessfully
            && world.Value.Result == contextId)
        {
            _frameIdToIsolatedWorld.TryRemove(new KeyValuePair<string, Lazy<Task<int>>>(frameId, world));
        }
    }

    /// <summary>
    /// Wakes every caller waiting on the frame, so each looks again at what it now has.
    /// </summary>
    private void SignalExecutionContextWaiters(string frameId)
    {
        if (_executionContextWaiters.TryRemove(frameId, out var waiters))
            waiters.TrySetResult();
    }

    /// <summary>
    /// Runs a command against a frame's main world, waiting for the frame's next one when it has
    /// none, and sending once more when the context it named turns out to be gone.
    /// </summary>
    /// <param name="frameId">The frame to run in.</param>
    /// <param name="command">Sends the command to the session and context it is handed.</param>
    /// <param name="ct">Bounds the wait for a context, alongside the page's own lifetime.</param>
    /// <remarks>
    /// A navigation's load event and its new context arrive on different loops, so a caller that
    /// starts work the moment a navigation completes can find the old document's context still
    /// recorded, or no context at all. The second case waits here for the new one. The first is
    /// caught when the browser rejects the old id, and is retried once against whatever has been
    /// recorded since.
    /// </remarks>
    internal Task<T> RunInFrameAsync<T>(
        string frameId, Func<ExecutionTarget, Task<T>> command, CancellationToken ct = default) =>
        RunInMainWorldAsync(frameId, mainFrameUsesDefault: false, command, ct);

    /// <summary>
    /// Runs a selector query against a frame's main world. The page's main frame, and anything
    /// that is not a frame of this page, uses the session's default context rather than a recorded
    /// id, which is what page-level locators have always sent and which cannot go stale.
    /// </summary>
    /// <param name="frame">The frame to run in, or null for the page's main frame.</param>
    /// <param name="command">Sends the command to the session and context it is handed.</param>
    /// <param name="ct">Bounds the wait for a context, alongside the page's own lifetime.</param>
    internal Task<T> RunInSelectorContextAsync<T>(
        IFrame? frame, Func<ExecutionTarget, Task<T>> command, CancellationToken ct = default)
    {
        if (frame is not Frame f || f.Id == SyntheticSelectorFrameId)
            return command(new ExecutionTarget(SessionFor(frame), null));

        return RunInMainWorldAsync(f.Id, mainFrameUsesDefault: true, command, ct);
    }

    private async Task<T> RunInMainWorldAsync<T>(
        string frameId, bool mainFrameUsesDefault, Func<ExecutionTarget, Task<T>> command, CancellationToken ct)
    {
        var target = await ResolveMainWorldAsync(frameId, mainFrameUsesDefault, ct).ConfigureAwait(false);

        try
        {
            return await command(target).ConfigureAwait(false);
        }
        catch (MotusProtocolException ex) when (target.ContextId is { } stale && IsStaleContextError(ex))
        {
            // The browser looks a context up before it compiles or runs anything, so a command it
            // turns away for naming a dead context has not run at all. Sending it once more cannot
            // run the caller's script twice, which is what makes this retry safe for commands that
            // change the page as well as for ones that only read it.
            ForgetExecutionContext(frameId, new FrameExecutionContext(stale, target.Session));
            target = await ResolveMainWorldAsync(frameId, mainFrameUsesDefault, ct).ConfigureAwait(false);
            return await command(target).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs a command against the frame's isolated world, making a fresh world and sending once
    /// more when the one it named has gone with the document it was made for.
    /// </summary>
    internal async Task<T> RunInIsolatedWorldAsync<T>(string frameId, Func<ExecutionTarget, Task<T>> command)
    {
        await WhenFrameReadyAsync(frameId).ConfigureAwait(false);
        _frames.TryGetValue(frameId, out var frame);

        var target = new ExecutionTarget(
            SessionFor(frame), await GetIsolatedWorldContextIdAsync(frameId).ConfigureAwait(false));

        try
        {
            return await command(target).ConfigureAwait(false);
        }
        catch (MotusProtocolException ex) when (target.ContextId is { } stale && IsStaleContextError(ex))
        {
            // Safe to send again for the reason given in RunInMainWorldAsync: a dead context is
            // refused before anything runs.
            DropIsolatedWorld(frameId, stale);
            target = new ExecutionTarget(
                SessionFor(frame), await GetIsolatedWorldContextIdAsync(frameId).ConfigureAwait(false));
            return await command(target).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Decides where a command about a frame's main world goes, waiting for the frame to report one
    /// when it has none.
    /// </summary>
    /// <remarks>
    /// The main frame never waits. With nothing recorded, the page session's default context is the
    /// main frame's current main world, so leaving the id out reaches the right document and cannot
    /// name a dead one. Any other frame has no such fallback: leaving the id out would run in the
    /// main frame instead, quietly and with the wrong answer, so it waits for its own context.
    /// A frame that has gone away fails at once rather than waiting for a context that will never
    /// come.
    /// </remarks>
    private async Task<ExecutionTarget> ResolveMainWorldAsync(
        string frameId, bool mainFrameUsesDefault, CancellationToken ct)
    {
        await WhenFrameReadyAsync(frameId).ConfigureAwait(false);
        _frames.TryGetValue(frameId, out var frame);

        // BiDi addresses script to a browsing context and has no context ids to wait for.
        if (_session is BiDiSession)
            return new ExecutionTarget(SessionFor(frame), null);

        var isMainFrame = string.Equals(frameId, _mainFrameId, StringComparison.Ordinal);
        if (isMainFrame && mainFrameUsesDefault)
            return new ExecutionTarget(_session, null);

        if (_frameIdToExecutionContext.TryGetValue(frameId, out var known))
            return new ExecutionTarget(known.Session, known.Id);

        if (isMainFrame)
            return new ExecutionTarget(_session, null);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _pageCts.Token);
        cts.CancelAfter(ExecutionContextWaitTimeout);

        while (true)
        {
            // Registered before looking, so a context recorded or a detach made between the look
            // and the wait still wakes this caller.
            var changed = _executionContextWaiters.GetOrAdd(
                frameId, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));

            if (_frameIdToExecutionContext.TryGetValue(frameId, out var context))
                return new ExecutionTarget(context.Session, context.Id);

            ThrowIfUnreachable(frameId, frame);

            try
            {
                await changed.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                if (_pageCts.IsCancellationRequested)
                    throw PageClosedWhileWaiting(frameId);

                throw new WaitTimeoutException(
                    condition: "frame execution context",
                    timeoutDuration: ExecutionContextWaitTimeout,
                    lastEvaluatedValue: null,
                    message: $"Frame '{frameId}' did not report an execution context within " +
                             $"{ExecutionContextWaitTimeout.TotalMilliseconds}ms.");
            }
        }
    }

    private void ThrowIfUnreachable(string frameId, Frame? frame)
    {
        if (_isClosed)
            throw PageClosedWhileWaiting(frameId);

        if (frame is null || frame.IsDetached || !_frames.ContainsKey(frameId))
            throw new MotusTargetClosedException("frame", frameId, $"Frame '{frameId}' was detached.");
    }

    private MotusTargetClosedException PageClosedWhileWaiting(string frameId) =>
        new("page", _targetId, $"The page was closed while waiting for frame '{frameId}' to report an execution context.");

    /// <summary>
    /// Drops a context the browser has said is gone, unless something newer has replaced it.
    /// </summary>
    private void ForgetExecutionContext(string frameId, FrameExecutionContext stale) =>
        _frameIdToExecutionContext.TryRemove(new KeyValuePair<string, FrameExecutionContext>(frameId, stale));

    private static bool IsStaleContextError(MotusProtocolException ex) =>
        ex.Message.Contains(StaleContextMessage, StringComparison.Ordinal);
}
