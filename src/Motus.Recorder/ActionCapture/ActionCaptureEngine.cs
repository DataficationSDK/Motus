using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Motus.Abstractions;
using Motus.Recorder.Records;
using Motus.Recorder.SelectorInference;

namespace Motus.Recorder.ActionCapture;

/// <summary>
/// Orchestrates the action capture pipeline: JS injection, CDP event subscriptions,
/// the <see cref="InputStateMachine"/> for debouncing/grouping, and selector inference.
/// </summary>
public sealed class ActionCaptureEngine : IActionCaptureEngine
{
    private readonly ActionCaptureOptions _options;
    private readonly SelectorInferenceOptions? _inferenceOptions;
    private readonly Channel<ActionRecord> _rawChannel;
    private readonly Channel<ResolvedAction> _resolvedChannel;
    private readonly List<ResolvedAction> _captured = [];
    private readonly object _capturedLock = new();

    private InputStateMachine? _stateMachine;
    private SelectorInferenceEngine? _inferenceEngine;
    private Task? _inferencePump;
    private CancellationTokenSource? _recordingCts;
    private Page? _page;
    private bool _disposed;

    // Dialog correlation state
    private string? _pendingDialogType;
    private string? _pendingDialogUrl;

    public ActionCaptureEngine(
        ActionCaptureOptions? options = null,
        SelectorInferenceOptions? inferenceOptions = null)
    {
        _options = options ?? new ActionCaptureOptions();
        _inferenceOptions = inferenceOptions;
        _rawChannel = Channel.CreateUnbounded<ActionRecord>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _resolvedChannel = Channel.CreateUnbounded<ResolvedAction>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    }

    public bool IsRecording => _recordingCts is not null && !_recordingCts.IsCancellationRequested;

    public IAsyncEnumerable<ResolvedAction> Actions => ReadResolvedActionsAsync();

    public IReadOnlyList<ResolvedAction> CapturedActions
    {
        get
        {
            lock (_capturedLock)
                return _captured.ToList();
        }
    }

    public async Task StartAsync(IPage page, CancellationToken ct = default)
    {
        if (_recordingCts is not null)
            throw new InvalidOperationException("Recording is already in progress.");

        _page = (Page)page;
        _recordingCts = new CancellationTokenSource();

        var rawWriter = new CapturingRawChannelWriter(_rawChannel.Writer);
        _stateMachine = new InputStateMachine(rawWriter, _options);

        // Build inference engine from registered strategies
        var strategies = _page.ContextInternal.SelectorStrategies.GetAllByPriority();
        _inferenceEngine = new SelectorInferenceEngine(strategies, _page, _inferenceOptions);

        // Start inference pump
        _inferencePump = PumpInferenceAsync(_recordingCts.Token);

        // Register binding for DOM events
        await _page.ExposeBindingInternalAsync(_options.BindingName, OnBindingCalledAsync);

        // Inject listener script for future navigations
        var script = RecorderScript.GetSource(_options.BindingName);
        await _page.AddInitScriptInternalAsync(script);

        // Also evaluate immediately on the current page
        try
        {
            await page.EvaluateAsync<object?>(script);
        }
        catch
        {
            // Page may not be ready yet; init script handles future navigations
        }

        // Use Page's internal events for navigation (avoids competing with Page's own CDP pump)
        _page.FrameNavigated += OnFrameNavigated;

        // Dialog: use Page's Dialog event for opening, CDP for closing (Page does not subscribe to closed)
        _page.Dialog += OnDialogEvent;

        var session = _page.Session;
        var linkedCt = _recordingCts.Token;

        _ = PumpCdpEventsAsync(
            session, "Page.javascriptDialogClosed",
            RecorderJsonContext.Default.RecorderDialogClosedEvent,
            OnDialogClosed, linkedCt);

        // File chooser via page event
        _page.FileChooser += OnFileChooser;

        // Auto-stop on page close
        _page.Close += OnPageClose;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        if (_recordingCts is null)
            return;

        // Flush pending debounced actions
        _stateMachine?.Flush();

        // Complete the raw channel so the inference pump drains and finishes
        _rawChannel.Writer.TryComplete();

        // Wait for inference pump to finish processing all raw actions
        if (_inferencePump is not null)
            await _inferencePump;

        // Cancel CDP event pumps
        await _recordingCts.CancelAsync();
        _recordingCts.Dispose();
        _recordingCts = null;

        // Complete the resolved channel
        _resolvedChannel.Writer.TryComplete();

        // Unhook page events
        if (_page is not null)
        {
            _page.FrameNavigated -= OnFrameNavigated;
            _page.Dialog -= OnDialogEvent;
            _page.FileChooser -= OnFileChooser;
            _page.Close -= OnPageClose;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_recordingCts is not null)
            await StopAsync();
        else
        {
            _rawChannel.Writer.TryComplete();
            _resolvedChannel.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Processes a raw DOM event JSON payload directly, bypassing the CDP binding callback path.
    /// Used by tests to inject events without going through the full transport stack.
    /// </summary>
    internal void ProcessDomEvent(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);
            if (payload is not null)
                _stateMachine?.ProcessEvent(payload);
        }
        catch
        {
            // Malformed payload; skip
        }
    }

    private static bool NeedsSelector(ActionRecord action)
        => action is ClickAction or FillAction or SelectAction or CheckAction or FileUploadAction;

    private async Task PumpInferenceAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var action in _rawChannel.Reader.ReadAllAsync(ct))
            {
                string? selector = null;

                if (NeedsSelector(action) && action.X is not null && action.Y is not null)
                {
                    try
                    {
                        selector = await _inferenceEngine!.InferAsync(
                            action.X.Value, action.Y.Value, action.TargetId, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Inference failed; selector stays null
                    }
                }

                var resolved = new ResolvedAction(action, selector);
                WriteResolved(resolved);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }

        // Drain any remaining items after channel completion (without CT)
        while (_rawChannel.Reader.TryRead(out var remaining))
        {
            WriteResolved(new ResolvedAction(remaining, Selector: null));
        }
    }

    private void WriteResolved(ResolvedAction resolved)
    {
        lock (_capturedLock)
            _captured.Add(resolved);
        _resolvedChannel.Writer.TryWrite(resolved);
    }

    private void WriteDirectResolved(ActionRecord action)
    {
        var resolved = new ResolvedAction(action, Selector: null);
        WriteResolved(resolved);
    }

    private Task<object?> OnBindingCalledAsync(object?[] args)
    {
        if (args.Length == 0)
            return Task.FromResult<object?>(null);

        // The binding payload arrives as a JsonElement (from the Page's generic deserializer)
        string? json = args[0] switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } je => je.GetString(),
            _ => args[0]?.ToString()
        };

        if (json is null)
            return Task.FromResult<object?>(null);

        try
        {
            var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);
            if (payload is not null)
                _stateMachine?.ProcessEvent(payload);
        }
        catch
        {
            // Malformed payload; skip
        }

        return Task.FromResult<object?>(null);
    }

    private void OnFrameNavigated(string url)
    {
        var action = new NavigationAction(
            Timestamp: DateTimeOffset.UtcNow,
            PageUrl: url,
            BackendNodeId: null,
            X: null,
            Y: null,
            Url: url
        );

        WriteDirectResolved(action);
    }

    private void OnDialogEvent(object? sender, DialogEventArgs e)
    {
        _pendingDialogType = e.Dialog.Type.ToString().ToLowerInvariant();
        _pendingDialogUrl = _page?.Url ?? string.Empty;
    }

    private void OnDialogClosed(RecorderDialogClosedEvent evt)
    {
        var action = new DialogAction(
            Timestamp: DateTimeOffset.UtcNow,
            PageUrl: _pendingDialogUrl ?? string.Empty,
            BackendNodeId: null,
            X: null,
            Y: null,
            DialogType: _pendingDialogType ?? "alert",
            Accepted: evt.Result,
            PromptText: evt.UserInput
        );

        WriteDirectResolved(action);
        _pendingDialogType = null;
        _pendingDialogUrl = null;
    }

    private void OnFileChooser(object? sender, IFileChooser chooser)
    {
        var action = new FileUploadAction(
            Timestamp: DateTimeOffset.UtcNow,
            PageUrl: _page?.Url ?? string.Empty,
            BackendNodeId: null,
            X: null,
            Y: null,
            FileNames: []
        );

        WriteDirectResolved(action);
    }

    private void OnPageClose(object? sender, EventArgs e)
    {
        _ = StopAsync();
    }

    private static async Task PumpCdpEventsAsync<TEvent>(
        IMotusSession session,
        string eventName,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TEvent> typeInfo,
        Action<TEvent> handler,
        CancellationToken ct)
    {
        try
        {
            await foreach (var evt in session.SubscribeAsync(eventName, typeInfo, ct))
            {
                try
                {
                    handler(evt);
                }
                catch
                {
                    // Prevent handler exceptions from killing the pump
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
    }

    private async IAsyncEnumerable<ResolvedAction> ReadResolvedActionsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var action in _resolvedChannel.Reader.ReadAllAsync(ct))
        {
            yield return action;
        }
    }

    /// <summary>
    /// A ChannelWriter wrapper for the raw channel. Does not capture to list (capture
    /// happens at the resolved channel level after inference).
    /// </summary>
    private sealed class CapturingRawChannelWriter(
        ChannelWriter<ActionRecord> inner) : ChannelWriter<ActionRecord>
    {
        public override bool TryWrite(ActionRecord item)
            => inner.TryWrite(item);

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
            => inner.WaitToWriteAsync(cancellationToken);

        public override bool TryComplete(Exception? error = null)
            => inner.TryComplete(error);
    }
}
