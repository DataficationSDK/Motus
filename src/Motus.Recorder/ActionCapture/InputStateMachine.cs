using System.Threading.Channels;
using Motus.Recorder.Records;

namespace Motus.Recorder.ActionCapture;

/// <summary>
/// Debounces and groups raw DOM event payloads into semantic <see cref="ActionRecord"/> objects.
/// Thread-safe: all mutations guarded by <c>_lock</c> since binding callbacks fire on threadpool threads.
/// </summary>
internal sealed class InputStateMachine
{
    private readonly ChannelWriter<ActionRecord> _writer;
    private readonly ActionCaptureOptions _options;
    private readonly Func<long> _clock;
    private readonly Func<TimeSpan, Action, IDisposable> _timerFactory;
    private readonly object _lock = new();

    // Click state
    private DomEventPayload? _pendingMouseDown;
    private long _mouseDownTime;

    // Fill state
    private string? _activeFillPageUrl;
    private double? _activeFillX;
    private double? _activeFillY;
    private int? _activeFillTargetId;
    private string _currentFillValue = string.Empty;
    private long _fillStartTime;
    private IDisposable? _fillTimer;

    // Scroll state
    private double _lastScrollX;
    private double _lastScrollY;
    private double? _scrollMouseX;
    private double? _scrollMouseY;
    private string? _scrollPageUrl;
    private long _scrollStartTime;
    private IDisposable? _scrollTimer;

    private string _lastPageUrl = string.Empty;

    internal InputStateMachine(
        ChannelWriter<ActionRecord> writer,
        ActionCaptureOptions options,
        Func<long>? clock = null,
        Func<TimeSpan, Action, IDisposable>? timerFactory = null)
    {
        _writer = writer;
        _options = options;
        _clock = clock ?? (() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        _timerFactory = timerFactory ?? DefaultTimerFactory;
    }

    internal void ProcessEvent(DomEventPayload evt)
    {
        lock (_lock)
        {
            if (evt.PageUrl is not null)
                _lastPageUrl = evt.PageUrl;

            switch (evt.Type)
            {
                case "mousedown":
                    HandleMouseDown(evt);
                    break;
                case "mouseup":
                    HandleMouseUp(evt);
                    break;
                case "input":
                    HandleInput(evt);
                    break;
                case "keydown":
                    HandleKeyDown(evt);
                    break;
                case "blur":
                    FlushFill();
                    break;
                case "change":
                    HandleChange(evt);
                    break;
                case "scroll":
                    HandleScroll(evt);
                    break;
            }
        }
    }

    internal void Flush()
    {
        lock (_lock)
        {
            FlushFill();
            FlushScroll();
            _pendingMouseDown = null;
        }
    }

    private void HandleMouseDown(DomEventPayload evt)
    {
        _pendingMouseDown = evt;
        _mouseDownTime = _clock();
    }

    private void HandleMouseUp(DomEventPayload evt)
    {
        if (_pendingMouseDown is null)
            return;

        var elapsed = _clock() - _mouseDownTime;
        if (elapsed > _options.ClickTimeThresholdMs)
        {
            _pendingMouseDown = null;
            return;
        }

        var dx = (evt.X ?? 0) - (_pendingMouseDown.X ?? 0);
        var dy = (evt.Y ?? 0) - (_pendingMouseDown.Y ?? 0);
        var distance = Math.Sqrt(dx * dx + dy * dy);

        if (distance > _options.ClickDistanceThreshold)
        {
            _pendingMouseDown = null;
            return;
        }

        var action = new ClickAction(
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(_mouseDownTime),
            PageUrl: _lastPageUrl,
            BackendNodeId: null,
            X: _pendingMouseDown.X,
            Y: _pendingMouseDown.Y,
            Button: _pendingMouseDown.Button ?? "left",
            ClickCount: _pendingMouseDown.ClickCount ?? 1,
            Modifiers: _pendingMouseDown.Modifiers ?? 0
        ) { TargetId = _pendingMouseDown.TargetId };

        _writer.TryWrite(action);
        _pendingMouseDown = null;
    }

    private void HandleInput(DomEventPayload evt)
    {
        if (_activeFillPageUrl is null)
        {
            _fillStartTime = _clock();
            _activeFillPageUrl = _lastPageUrl;
            _activeFillX = evt.X;
            _activeFillY = evt.Y;
            _activeFillTargetId = evt.TargetId;
        }

        _currentFillValue = evt.Value ?? string.Empty;
        ResetFillTimer();
    }

    private void HandleKeyDown(DomEventPayload evt)
    {
        var key = evt.Key ?? string.Empty;

        if (IsPrintableKey(key) && _activeFillPageUrl is not null)
            return;

        if (!IsPrintableKey(key) && _activeFillPageUrl is not null)
            FlushFill();

        if (!IsPrintableKey(key))
        {
            var action = new KeyPressAction(
                Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(_clock()),
                PageUrl: _lastPageUrl,
                BackendNodeId: null,
                X: evt.X,
                Y: evt.Y,
                Key: key,
                Code: evt.Code ?? string.Empty,
                Modifiers: evt.Modifiers ?? 0
            );

            _writer.TryWrite(action);
        }
    }

    private void HandleChange(DomEventPayload evt)
    {
        var tagName = evt.TagName?.ToUpperInvariant();
        var inputType = evt.InputType?.ToLowerInvariant();

        if (tagName == "SELECT")
        {
            var action = new SelectAction(
                Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(_clock()),
                PageUrl: _lastPageUrl,
                BackendNodeId: null,
                X: evt.X,
                Y: evt.Y,
                SelectedValues: evt.SelectedValues ?? []
            ) { TargetId = evt.TargetId };
            _writer.TryWrite(action);
        }
        else if (inputType is "checkbox" or "radio")
        {
            var action = new CheckAction(
                Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(_clock()),
                PageUrl: _lastPageUrl,
                BackendNodeId: null,
                X: evt.X,
                Y: evt.Y,
                Checked: evt.Checked ?? false
            ) { TargetId = evt.TargetId };
            _writer.TryWrite(action);
        }
    }

    private void HandleScroll(DomEventPayload evt)
    {
        if (_scrollPageUrl is null)
        {
            _scrollStartTime = _clock();
            _scrollMouseX = evt.MouseX;
            _scrollMouseY = evt.MouseY;
        }

        _scrollPageUrl = _lastPageUrl;
        _lastScrollX = evt.ScrollX ?? 0;
        _lastScrollY = evt.ScrollY ?? 0;
        ResetScrollTimer();
    }

    private void FlushFill()
    {
        _fillTimer?.Dispose();
        _fillTimer = null;

        if (_activeFillPageUrl is null)
            return;

        var action = new FillAction(
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(_fillStartTime),
            PageUrl: _activeFillPageUrl,
            BackendNodeId: null,
            X: _activeFillX,
            Y: _activeFillY,
            Value: _currentFillValue
        ) { TargetId = _activeFillTargetId };

        _writer.TryWrite(action);
        _activeFillPageUrl = null;
        _currentFillValue = string.Empty;
        _activeFillX = null;
        _activeFillY = null;
        _activeFillTargetId = null;
    }

    private void FlushScroll()
    {
        _scrollTimer?.Dispose();
        _scrollTimer = null;

        if (_scrollPageUrl is null)
            return;

        var action = new ScrollAction(
            Timestamp: DateTimeOffset.FromUnixTimeMilliseconds(_scrollStartTime),
            PageUrl: _scrollPageUrl,
            BackendNodeId: null,
            X: _scrollMouseX,
            Y: _scrollMouseY,
            ScrollX: _lastScrollX,
            ScrollY: _lastScrollY
        );

        _writer.TryWrite(action);
        _scrollPageUrl = null;
        _scrollMouseX = null;
        _scrollMouseY = null;
    }

    private void ResetFillTimer()
    {
        _fillTimer?.Dispose();
        _fillTimer = _timerFactory(
            TimeSpan.FromMilliseconds(_options.FillDebounceMs),
            () => { lock (_lock) { FlushFill(); } });
    }

    private void ResetScrollTimer()
    {
        _scrollTimer?.Dispose();
        _scrollTimer = _timerFactory(
            TimeSpan.FromMilliseconds(_options.ScrollDebounceMs),
            () => { lock (_lock) { FlushScroll(); } });
    }

    private static bool IsPrintableKey(string key)
        => key.Length == 1 && !char.IsControl(key[0]);

    private static IDisposable DefaultTimerFactory(TimeSpan delay, Action callback)
    {
        var cts = new CancellationTokenSource();
        _ = Task.Delay(delay, cts.Token).ContinueWith(
            _ => callback(),
            cts.Token,
            TaskContinuationOptions.OnlyOnRanToCompletion,
            TaskScheduler.Default);
        return cts;
    }
}
