using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    private void StartEventPump()
    {
        var ct = _pageCts.Token;

        // Frame navigation
        _ = PumpEventsAsync(
            "Page.frameNavigated",
            CdpJsonContext.Default.PageFrameNavigatedEvent,
            OnFrameNavigated, ct);

        // Frame attached/detached
        _ = PumpEventsAsync(
            "Page.frameAttached",
            CdpJsonContext.Default.PageFrameAttachedEvent,
            OnFrameAttached, ct);

        _ = PumpEventsAsync(
            "Page.frameDetached",
            CdpJsonContext.Default.PageFrameDetachedEvent,
            OnFrameDetached, ct);

        // Lifecycle events
        _ = PumpEventsAsync(
            "Page.loadEventFired",
            CdpJsonContext.Default.PageLoadEventFiredEvent,
            _ => LoadEventFired?.Invoke(), ct);

        _ = PumpEventsAsync(
            "Page.domContentEventFired",
            CdpJsonContext.Default.PageDomContentEventFiredEvent,
            _ => DomContentEventFired?.Invoke(), ct);

        // Execution contexts
        _ = PumpEventsAsync(
            "Runtime.executionContextCreated",
            CdpJsonContext.Default.RuntimeExecutionContextCreatedEvent,
            OnExecutionContextCreated, ct);

        // Console
        _ = PumpEventsAsync(
            "Runtime.consoleAPICalled",
            CdpJsonContext.Default.RuntimeConsoleApiCalledEvent,
            OnConsoleApiCalled, ct);

        // Exceptions
        _ = PumpEventsAsync(
            "Runtime.exceptionThrown",
            CdpJsonContext.Default.RuntimeExceptionThrownEvent,
            OnExceptionThrown, ct);

        // Dialogs
        _ = PumpEventsAsync(
            "Page.javascriptDialogOpening",
            CdpJsonContext.Default.PageJavascriptDialogOpeningEvent,
            OnDialogOpening, ct);

        // Downloads
        _ = PumpEventsAsync(
            "Page.downloadWillBegin",
            CdpJsonContext.Default.PageDownloadWillBeginEvent,
            OnDownloadWillBegin, ct);

        _ = PumpEventsAsync(
            "Page.downloadProgress",
            CdpJsonContext.Default.PageDownloadProgressEvent,
            OnDownloadProgress, ct);

        // File chooser
        _ = PumpEventsAsync(
            "Page.fileChooserOpened",
            CdpJsonContext.Default.PageFileChooserOpenedEvent,
            OnFileChooserOpened, ct);

        // Bindings
        _ = PumpEventsAsync(
            "Runtime.bindingCalled",
            CdpJsonContext.Default.RuntimeBindingCalledEvent,
            OnBindingCalled, ct);

        // Target attached (for workers)
        _ = PumpEventsAsync(
            "Target.attachedToTarget",
            CdpJsonContext.Default.TargetAttachedToTargetEvent,
            OnTargetAttached, ct);

        // Fetch auth required (HTTP credentials)
        if (_context.Options?.HttpCredentials is not null)
        {
            _ = PumpEventsAsync(
                "Fetch.authRequired",
                CdpJsonContext.Default.FetchAuthRequiredEvent,
                OnFetchAuthRequired, ct);
        }
    }

    private async Task PumpEventsAsync<T>(
        string eventName,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Action<T> handler,
        CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _session.SubscribeAsync(eventName, typeInfo, ct).ConfigureAwait(false))
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

    private void OnFrameNavigated(PageFrameNavigatedEvent evt)
    {
        var info = evt.Frame;
        var frame = _frames.GetOrAdd(info.Id, id => new Frame(this, id, info.ParentId));

        frame.Url = info.Url;
        frame.Name = info.Name;

        // First frame navigated is the main frame
        _mainFrameId ??= info.Id;

        // Notify internal subscribers (e.g. Recorder)
        if (info.ParentId is null)
            FrameNavigated?.Invoke(info.Url);
    }

    private void OnFrameAttached(PageFrameAttachedEvent evt)
    {
        _frames.GetOrAdd(evt.FrameId, id => new Frame(this, id, evt.ParentFrameId));
    }

    private void OnFrameDetached(PageFrameDetachedEvent evt)
    {
        _frames.TryRemove(evt.FrameId, out _);
        _frameIdToExecutionContext.TryRemove(evt.FrameId, out _);
    }

    private void OnExecutionContextCreated(RuntimeExecutionContextCreatedEvent evt)
    {
        var ctx = evt.Context;

        // Extract frameId from auxData if present
        string? frameId = null;
        if (ctx.AuxData is JsonElement aux && aux.ValueKind == JsonValueKind.Object)
        {
            if (aux.TryGetProperty("frameId", out var fid))
                frameId = fid.GetString();
        }

        if (frameId is not null)
        {
            _frameIdToExecutionContext[frameId] = ctx.Id;
            _executionContextToFrameId[ctx.Id] = frameId;
        }
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

    private void OnTargetAttached(TargetAttachedToTargetEvent evt)
    {
        // Track workers internally; no public API yet
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
