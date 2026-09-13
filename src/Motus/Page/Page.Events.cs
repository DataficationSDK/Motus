using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    /// <summary>
    /// The reason <c>Page.frameDetached</c> carries when a frame has changed renderer process
    /// rather than gone away.
    /// </summary>
    private const string SwapDetachReason = "swap";

    private void StartEventPump()
    {
        var ct = _pageCts.Token;

        // Everything a target reports about the frames it hosts. The page session hosts most
        // frames, but not one that renders in its own process, so this same set is subscribed
        // again for every frame target that is adopted.
        StartFrameStructureEventPump(_session, ct);

        // Lifecycle events
        _ = PumpEventsAsync(_session,
            "Page.loadEventFired",
            CdpJsonContext.Default.PageLoadEventFiredEvent,
            _ => LoadEventFired?.Invoke(), ct);

        _ = PumpEventsAsync(_session,
            "Page.domContentEventFired",
            CdpJsonContext.Default.PageDomContentEventFiredEvent,
            _ => DomContentEventFired?.Invoke(), ct);

        // Dialogs
        _ = PumpEventsAsync(_session,
            "Page.javascriptDialogOpening",
            CdpJsonContext.Default.PageJavascriptDialogOpeningEvent,
            OnDialogOpening, ct);

        // Downloads
        _ = PumpEventsAsync(_session,
            "Page.downloadWillBegin",
            CdpJsonContext.Default.PageDownloadWillBeginEvent,
            OnDownloadWillBegin, ct);

        _ = PumpEventsAsync(_session,
            "Page.downloadProgress",
            CdpJsonContext.Default.PageDownloadProgressEvent,
            OnDownloadProgress, ct);

        // File chooser
        _ = PumpEventsAsync(_session,
            "Page.fileChooserOpened",
            CdpJsonContext.Default.PageFileChooserOpenedEvent,
            OnFileChooserOpened, ct);

        // Bindings
        _ = PumpEventsAsync(_session,
            "Runtime.bindingCalled",
            CdpJsonContext.Default.RuntimeBindingCalledEvent,
            OnBindingCalled, ct);

        // Fetch auth required (HTTP credentials), requires CDP Fetch domain
        if (_context.Options?.HttpCredentials is not null
            && (_session.Capabilities & MotusCapabilities.FetchInterception) != 0)
        {
            _ = PumpEventsAsync(_session,
                "Fetch.authRequired",
                CdpJsonContext.Default.FetchAuthRequiredEvent,
                OnFetchAuthRequired, ct);
        }
    }

    /// <summary>
    /// Subscribes, on one session, everything that session reports about the frames it hosts.
    /// </summary>
    /// <remarks>
    /// Called once for the page session and once for every frame target adopted afterwards. A
    /// frame in its own process reports its tree, its execution contexts, its console output and
    /// its own nested targets nowhere else, so a session that is not pumped is a subtree Motus
    /// cannot see. Console output and uncaught errors are raised on the owning page rather than
    /// being scoped to the frame, so a caller watching the page sees everything in it.
    /// </remarks>
    internal void StartFrameStructureEventPump(IMotusSession session, CancellationToken ct)
    {
        var isPageSession = ReferenceEquals(session, _session);

        // The three events that shape the frame tree are read together, in the order the browser
        // sent them. Each on a channel of its own would be handled by a loop of its own, and a
        // child's attach could then be acted on before the navigation of its parent that came
        // first on the wire, which would drop a frame the new document had only just added.
        _ = PumpFrameTreeEventsAsync(session, isPageSession, ct);

        // Per-frame load completion, used to wait out a navigation of a single frame.
        _ = PumpEventsAsync(session,
            "Page.frameStoppedLoading",
            CdpJsonContext.Default.PageFrameStoppedLoadingEvent,
            evt => FrameStoppedLoading?.Invoke(evt.FrameId), ct);

        _ = PumpEventsAsync(session,
            "Runtime.executionContextCreated",
            CdpJsonContext.Default.RuntimeExecutionContextCreatedEvent,
            OnExecutionContextCreated, ct);

        _ = PumpEventsAsync(session,
            "Runtime.consoleAPICalled",
            CdpJsonContext.Default.RuntimeConsoleApiCalledEvent,
            OnConsoleApiCalled, ct);

        _ = PumpEventsAsync(session,
            "Runtime.exceptionThrown",
            CdpJsonContext.Default.RuntimeExceptionThrownEvent,
            OnExceptionThrown, ct);

        _ = PumpEventsAsync(session,
            "Target.attachedToTarget",
            CdpJsonContext.Default.TargetAttachedToTargetEvent,
            OnTargetAttached, ct);

        _ = PumpEventsAsync(session,
            "Target.detachedFromTarget",
            CdpJsonContext.Default.TargetDetachedFromTargetEvent,
            OnTargetDetached, ct);
    }

    private async Task PumpFrameTreeEventsAsync(IMotusSession session, bool isPageSession, CancellationToken ct)
    {
        string[] events = ["Page.frameNavigated", "Page.frameAttached", "Page.frameDetached"];

        try
        {
            await foreach (var raw in session.SubscribeAsync(events, ct).ConfigureAwait(false))
            {
                try
                {
                    switch (raw.Method)
                    {
                        case "Page.frameNavigated":
                            if (JsonSerializer.Deserialize(raw.Params, CdpJsonContext.Default.PageFrameNavigatedEvent) is { } navigated)
                                OnFrameNavigated(navigated, session, isPageSession);
                            break;
                        case "Page.frameAttached":
                            if (JsonSerializer.Deserialize(raw.Params, CdpJsonContext.Default.PageFrameAttachedEvent) is { } attached)
                                OnFrameAttached(attached, session);
                            break;
                        case "Page.frameDetached":
                            if (JsonSerializer.Deserialize(raw.Params, CdpJsonContext.Default.PageFrameDetachedEvent) is { } detached)
                                OnFrameDetached(detached, session);
                            break;
                    }
                }
                catch
                {
                    // Prevent user handler exceptions from killing the event pump
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on page close
        }
    }

    private async Task PumpEventsAsync<T>(
        IMotusSession session,
        string eventName,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Action<T> handler,
        CancellationToken ct)
    {
        try
        {
            await foreach (var evt in session.SubscribeAsync(eventName, typeInfo, ct).ConfigureAwait(false))
            {
                try
                {
                    handler(evt);
                }
                catch
                {
                    // Prevent user handler exceptions from killing the event pump
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on page close
        }
    }

    private void OnFrameNavigated(PageFrameNavigatedEvent evt, IMotusSession source, bool isPageSession)
    {
        var info = evt.Frame;
        var frame = EnsureFrame(info.Id, info.ParentId);

        // A new document replaces everything the old one hosted, so the frames underneath are gone
        // whatever the browser goes on to say about them. It does not always say they were
        // removed: a document kept in the back-forward cache has its frames announced as swapped
        // out instead, and those would otherwise be listed alongside the new document's for as
        // long as the page lived.
        DetachChildFrames(info.Id);

        frame.Url = info.Url;
        frame.Name = info.Name;
        RecordFrameOwnership(info.Id, source, isPageSession);

        // A navigating frame gets a fresh main world, so the isolated world made against the old
        // one is gone with it and asking for one again has to create it.
        _frameIdToIsolatedWorld.TryRemove(info.Id, out _);

        // The first frame the page session reports is the main frame. A frame target reports its
        // own root here too, and claiming that as the page's main frame would rewrite what the
        // page is whenever a cross-origin frame happens to navigate first.
        if (isPageSession)
            _mainFrameId ??= info.Id;

        FrameNavigated?.Invoke(this, frame);

        // Notify internal subscribers (e.g. Recorder)
        if (isPageSession && info.ParentId is null)
            MainFrameNavigated?.Invoke(info.Url);
    }

    private void OnFrameAttached(PageFrameAttachedEvent evt, IMotusSession source)
    {
        var frame = EnsureFrame(evt.FrameId, evt.ParentFrameId);
        RecordFrameOwnership(evt.FrameId, source, ReferenceEquals(source, _session));
        FrameAttached?.Invoke(this, frame);
    }

    /// <summary>
    /// Drops a frame the browser says has gone away, unless it is only changing renderer process.
    /// </summary>
    /// <remarks>
    /// A frame moved into another process is announced as a detach carrying the reason
    /// <c>swap</c>, and that frame has not gone anywhere: the target now hosting it reports it
    /// again through its own frame tree. The two announcements travel over different sessions, so
    /// either can arrive first. When the new target has already reported the frame, the frame is
    /// recorded against that target's session rather than the one delivering the notice, and the
    /// notice is late news to be ignored: acting on it would remove a frame the page still has,
    /// send its traffic back to the wrong session, and mark a handle the caller is holding as
    /// detached for good. When the notice comes first, the frame is kept for the target about to
    /// claim it, and only the frames it hosted are dropped, since the new document reports its
    /// own.
    /// <para>
    /// A swap can also mean a document going into the back-forward cache when its parent navigates
    /// away. Nothing here tells that apart, and nothing needs to: the parent's navigation drops
    /// every frame underneath it (see <see cref="OnFrameNavigated"/>), so the notice finds nothing
    /// left to keep.
    /// </para>
    /// </remarks>
    private void OnFrameDetached(PageFrameDetachedEvent evt, IMotusSession source)
    {
        if (string.Equals(evt.Reason, SwapDetachReason, StringComparison.Ordinal))
        {
            if (_frameIdToSession.TryGetValue(evt.FrameId, out var owner) && !ReferenceEquals(owner, source))
                return;

            DetachChildFrames(evt.FrameId);
            return;
        }

        DetachFrame(evt.FrameId);
    }

    /// <summary>
    /// Drops the frames hosted underneath <paramref name="frameId"/>, deepest first, leaving the
    /// frame itself in place.
    /// </summary>
    private void DetachChildFrames(string frameId)
    {
        foreach (var child in GetChildFrames(frameId).Cast<Frame>())
        {
            DetachChildFrames(child.Id);
            DetachFrame(child.Id);
        }
    }

    /// <summary>
    /// Drops one frame and everything recorded about it, and tells subscribers it has gone.
    /// </summary>
    private void DetachFrame(string frameId)
    {
        var frame = RemoveFrame(frameId);
        _frameIdToExecutionContext.TryRemove(frameId, out _);
        _frameIdToSession.TryRemove(frameId, out _);
        _frameIdToIsolatedWorld.TryRemove(frameId, out _);
        _frameTargetInit.TryRemove(frameId, out _);

        if (frame is null)
            return;

        frame.MarkDetached();
        FrameDetached?.Invoke(this, frame);
    }

    /// <summary>
    /// Notes which session a frame is reached over, when that is not the page's own.
    /// </summary>
    /// <remarks>
    /// Frames the page session reports record nothing, so <see cref="SessionFor"/> falls through to
    /// the page session for them and every page-level round trip stays exactly what it was. Frames
    /// reported by any other session are recorded, which covers a frame in its own process and also
    /// the ordinary same-process children inside it, whose contexts live on their host's session
    /// rather than the page's.
    /// </remarks>
    private void RecordFrameOwnership(string frameId, IMotusSession source, bool isPageSession)
    {
        if (!isPageSession)
            _frameIdToSession[frameId] = source;
    }

    private void OnExecutionContextCreated(RuntimeExecutionContextCreatedEvent evt)
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

        if (frameId is not null && isDefaultWorld)
            _frameIdToExecutionContext[frameId] = ctx.Id;
    }

    private void OnConsoleApiCalled(RuntimeConsoleApiCalledEvent evt)
    {
        var text = string.Join(" ", evt.Args.Select(a =>
            a.Value?.ToString() ?? a.Description ?? a.Type));
        var args = new ConsoleMessageEventArgs(evt.Type, text);
        Console?.Invoke(this, args);
        _ = _context.LifecycleHooks.FireOnConsoleMessageAsync(this, args);
    }

    private void OnExceptionThrown(RuntimeExceptionThrownEvent evt)
    {
        var details = evt.ExceptionDetails;
        var message = details.Exception?.Description ?? details.Text;
        var stack = details.Exception?.Description;
        var args = new PageErrorEventArgs(message, stack);
        PageError?.Invoke(this, args);
        _ = _context.LifecycleHooks.FireOnPageErrorAsync(this, args);
    }

    private void OnDialogOpening(PageJavascriptDialogOpeningEvent evt)
    {
        var dialogType = evt.Type switch
        {
            "alert" => DialogType.Alert,
            "confirm" => DialogType.Confirm,
            "prompt" => DialogType.Prompt,
            "beforeunload" => DialogType.BeforeUnload,
            _ => DialogType.Alert
        };

        var dialog = new Dialog(_session, dialogType, evt.Message, evt.DefaultPrompt);
        Dialog?.Invoke(this, new DialogEventArgs(dialog));
    }

    private void OnDownloadWillBegin(PageDownloadWillBeginEvent evt)
    {
        var download = new Motus.Download(evt.Guid, evt.Url, evt.SuggestedFilename);
        _downloads[evt.Guid] = download;
        Download?.Invoke(this, download);
    }

    private void OnDownloadProgress(PageDownloadProgressEvent evt)
    {
        if (_downloads.TryGetValue(evt.Guid, out var download))
        {
            download.OnProgress(evt.State);
        }
    }

    private void OnFileChooserOpened(PageFileChooserOpenedEvent evt)
    {
        var chooser = new Motus.FileChooser(
            this,
            evt.Mode == "selectMultiple",
            evt.BackendNodeId);
        FileChooser?.Invoke(this, chooser);
    }

    private void OnBindingCalled(RuntimeBindingCalledEvent evt)
    {
        if (_bindings.TryGetValue(evt.Name, out var callback))
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    object?[] args;
                    try
                    {
                        args = JsonSerializer.Deserialize<object?[]>(evt.Payload) ?? [];
                    }
                    catch (JsonException)
                    {
                        // Payload is a single value (e.g. a JSON object string), not an array
                        args = [JsonSerializer.Deserialize<JsonElement>(evt.Payload)];
                    }
                    await callback(args).ConfigureAwait(false);
                }
                catch
                {
                    // Binding invocation failures are silently swallowed
                }
            });
        }
    }

    private void OnFetchAuthRequired(FetchAuthRequiredEvent evt)
    {
        var creds = _context.Options?.HttpCredentials;
        if (creds is null)
            return;

        _ = Task.Run(async () =>
        {
            try
            {
                await _session.SendAsync(
                    "Fetch.continueWithAuth",
                    new FetchContinueWithAuthParams(
                        evt.RequestId,
                        new FetchAuthChallengeResponse(
                            Response: "ProvideCredentials",
                            Username: creds.Username,
                            Password: creds.Password)),
                    CdpJsonContext.Default.FetchContinueWithAuthParams,
                    CdpJsonContext.Default.FetchContinueWithAuthResult,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Session may be gone
            }
        });
    }
}
