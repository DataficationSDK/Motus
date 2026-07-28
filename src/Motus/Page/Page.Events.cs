using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
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

        _ = PumpEventsAsync(session,
            "Page.frameNavigated",
            CdpJsonContext.Default.PageFrameNavigatedEvent,
            evt => OnFrameNavigated(evt, session, isPageSession), ct);

        _ = PumpEventsAsync(session,
            "Page.frameAttached",
            CdpJsonContext.Default.PageFrameAttachedEvent,
            evt => OnFrameAttached(evt, session), ct);

        _ = PumpEventsAsync(session,
            "Page.frameDetached",
            CdpJsonContext.Default.PageFrameDetachedEvent,
            OnFrameDetached, ct);

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
        var frame = _frames.GetOrAdd(info.Id, id => new Frame(this, id, info.ParentId));

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
        var frame = _frames.GetOrAdd(evt.FrameId, id => new Frame(this, id, evt.ParentFrameId));
        RecordFrameOwnership(evt.FrameId, source, ReferenceEquals(source, _session));
        FrameAttached?.Invoke(this, frame);
    }

    private void OnFrameDetached(PageFrameDetachedEvent evt)
    {
        _frames.TryRemove(evt.FrameId, out var frame);
        _frameIdToExecutionContext.TryRemove(evt.FrameId, out _);
        _frameIdToSession.TryRemove(evt.FrameId, out _);
        _frameIdToIsolatedWorld.TryRemove(evt.FrameId, out _);
        _frameTargetInit.TryRemove(evt.FrameId, out _);

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
