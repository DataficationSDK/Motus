using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using Motus.Abstractions;
using Motus.Recorder.Records;

namespace Motus.Recorder.ActionCapture;

/// <summary>
/// Orchestrates the action capture pipeline: JS injection, CDP event subscriptions,
/// and the <see cref="InputStateMachine"/> for debouncing/grouping.
/// </summary>
public sealed class ActionCaptureEngine : IActionCaptureEngine
{
    private readonly ActionCaptureOptions _options;
    private readonly Channel<ActionRecord> _channel;
    private readonly List<ActionRecord> _captured = [];
    private readonly object _capturedLock = new();

    private InputStateMachine? _stateMachine;
    private CancellationTokenSource? _recordingCts;
    private Page? _page;
    private bool _disposed;

    // Dialog correlation state
    private string? _pendingDialogType;
    private string? _pendingDialogUrl;

    public ActionCaptureEngine(ActionCaptureOptions? options = null)
    {
        _options = options ?? new ActionCaptureOptions();
        _channel = Channel.CreateUnbounded<ActionRecord>(
            new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
    }

    public bool IsRecording => _recordingCts is not null && !_recordingCts.IsCancellationRequested;

    public IAsyncEnumerable<ActionRecord> Actions => ReadActionsAsync();

    public IReadOnlyList<ActionRecord> CapturedActions
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

        var capturingWriter = new CapturingChannelWriter(_channel.Writer, _captured, _capturedLock);
        _stateMachine = new InputStateMachine(capturingWriter, _options);

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

        // Cancel CDP event pumps
        await _recordingCts.CancelAsync();
        _recordingCts.Dispose();
        _recordingCts = null;

        // Complete the channel
        _channel.Writer.TryComplete();

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
            _channel.Writer.TryComplete();
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

    private void WriteAction(ActionRecord action)
    {
        lock (_capturedLock)
            _captured.Add(action);
        _channel.Writer.TryWrite(action);
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

        WriteAction(action);
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

        WriteAction(action);
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

        WriteAction(action);
    }

    private void OnPageClose(object? sender, EventArgs e)
    {
        _ = StopAsync();
    }

    private static async Task PumpCdpEventsAsync<TEvent>(
        CdpSession session,
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

    private async IAsyncEnumerable<ActionRecord> ReadActionsAsync(
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var action in _channel.Reader.ReadAllAsync(ct))
        {
            yield return action;
        }
    }

    /// <summary>
    /// A ChannelWriter wrapper that captures every written action to the captured list
    /// before delegating to the underlying writer. This ensures InputStateMachine writes
    /// are visible in CapturedActions without a separate forwarding task.
    /// </summary>
    private sealed class CapturingChannelWriter(
        ChannelWriter<ActionRecord> inner,
        List<ActionRecord> captured,
        object capturedLock) : ChannelWriter<ActionRecord>
    {
        public override bool TryWrite(ActionRecord item)
        {
            lock (capturedLock)
                captured.Add(item);
            return inner.TryWrite(item);
        }

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
            => inner.WaitToWriteAsync(cancellationToken);

        public override bool TryComplete(Exception? error = null)
            => inner.TryComplete(error);
    }
}
