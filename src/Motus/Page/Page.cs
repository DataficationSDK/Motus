using System.Collections.Concurrent;
using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents a single browser page (tab).
/// </summary>
internal sealed partial class Page : IPage
{
    private readonly CdpSession _session;
    private readonly BrowserContext _context;
    private readonly string _targetId;
    private readonly CancellationTokenSource _pageCts = new();

    private readonly ConcurrentDictionary<string, Frame> _frames = new();
    private readonly ConcurrentDictionary<int, string> _executionContextToFrameId = new();
    private readonly ConcurrentDictionary<string, int> _frameIdToExecutionContext = new();
    private readonly ConcurrentDictionary<string, Download> _downloads = new();
    private readonly ConcurrentDictionary<string, Func<object?[], Task<object?>>> _bindings = new();
    private readonly List<string> _initScripts = [];

    private readonly Keyboard _keyboard;
    private readonly Mouse _mouse;
    private readonly Touchscreen _touchscreen;

    private string? _mainFrameId;
    private ViewportSize? _viewportSize;
    private volatile bool _isClosed;
    private VideoRecorder? _videoRecorder;

    internal Page(CdpSession session, BrowserContext context, string targetId)
    {
        _session = session;
        _context = context;
        _targetId = targetId;
        _keyboard = new Keyboard(session, _pageCts.Token);
        _mouse = new Mouse(session, _pageCts.Token);
        _touchscreen = new Touchscreen(session, _pageCts.Token);
    }

    public IBrowserContext Context => _context;

    public IFrame MainFrame =>
        _mainFrameId is not null && _frames.TryGetValue(_mainFrameId, out var frame)
            ? frame
            : throw new InvalidOperationException("Main frame has not been initialized.");

    public IReadOnlyList<IFrame> Frames => _frames.Values.ToList();

    public string Url => _mainFrameId is not null && _frames.TryGetValue(_mainFrameId, out var f)
        ? f.Url
        : string.Empty;

    public bool IsClosed => _isClosed;

    public ViewportSize? ViewportSize => _viewportSize;

    public IVideo? Video => _videoRecorder?.Video;

    public IKeyboard Keyboard => _keyboard;

    public IMouse Mouse => _mouse;

    public ITouchscreen Touchscreen => _touchscreen;

    // --- Events ---
    public event EventHandler? Close;
    public event EventHandler<ConsoleMessageEventArgs>? Console;
    public event EventHandler<DialogEventArgs>? Dialog;
    public event EventHandler<IDownload>? Download;
    public event EventHandler<IFileChooser>? FileChooser;
    public event EventHandler<PageErrorEventArgs>? PageError;
    public event EventHandler<IPage>? Popup;
    public event EventHandler<RequestEventArgs>? Request;
    public event EventHandler<RequestEventArgs>? RequestFailed;
    public event EventHandler<RequestEventArgs>? RequestFinished;
    public event EventHandler<ResponseEventArgs>? Response;

    // --- Internal events for extensions (e.g. Recorder) ---
    internal event Action<string>? FrameNavigated;
    internal event Action<string, bool, string?>? DialogHandled;

    internal CdpSession Session => _session;

    internal CancellationToken PageLifetimeToken => _pageCts.Token;

    internal BrowserContext ContextInternal => _context;

    internal Keyboard KeyboardInternal => _keyboard;

    internal Mouse MouseInternal => _mouse;

    internal Touchscreen TouchscreenInternal => _touchscreen;

    internal void SetVideoRecorder(VideoRecorder recorder) => _videoRecorder = recorder;

    internal async Task InitializeAsync(CancellationToken ct)
    {
        // Subscribe to event channels BEFORE enabling CDP domains.
        // Chrome fires events immediately after Page.enable / Runtime.enable,
        // so the pump must already be listening or early events are dropped
        // (e.g. the initial executionContextCreated that provides the main-frame context ID).
        StartEventPump();

        await _session.SendAsync("Page.enable", CdpJsonContext.Default.PageEnableResult, ct).ConfigureAwait(false);
        await _session.SendAsync("Runtime.enable", CdpJsonContext.Default.RuntimeEnableResult, ct).ConfigureAwait(false);

        // Enable file chooser interception
        await _session.SendAsync(
            "Page.setInterceptFileChooserDialog",
            new PageSetInterceptFileChooserDialogParams(Enabled: true),
            CdpJsonContext.Default.PageSetInterceptFileChooserDialogParams,
            ct).ConfigureAwait(false);

        // Apply any init scripts from the context
        foreach (var script in _context.InitScripts)
        {
            await _session.SendAsync(
                "Page.addScriptToEvaluateOnNewDocument",
                new PageAddScriptToEvaluateOnNewDocumentParams(script),
                CdpJsonContext.Default.PageAddScriptToEvaluateOnNewDocumentParams,
                CdpJsonContext.Default.PageAddScriptToEvaluateOnNewDocumentResult,
                ct).ConfigureAwait(false);
        }

        // Apply any bindings from the context
        foreach (var (name, callback) in _context.Bindings)
        {
            _bindings[name] = callback;
            await _session.SendAsync(
                "Runtime.addBinding",
                new RuntimeAddBindingParams(name),
                CdpJsonContext.Default.RuntimeAddBindingParams,
                CdpJsonContext.Default.RuntimeAddBindingResult,
                ct).ConfigureAwait(false);
        }

        // Initialize network monitoring and interception
        await InitializeNetworkAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the main frame if available, or a lightweight synthetic frame
    /// for selector strategy dispatch. The synthetic frame is not stored in
    /// the page's frame collection so it doesn't affect frame enumeration.
    /// </summary>
    internal IFrame GetFrameForSelectors() =>
        _mainFrameId is not null && _frames.TryGetValue(_mainFrameId, out var frame)
            ? frame
            : new Frame(this, "__selector__", parentFrameId: null);

    internal bool TryGetFrame(string frameId, out Frame? frame) =>
        _frames.TryGetValue(frameId, out frame);

    internal IReadOnlyList<IFrame> GetChildFrames(string parentFrameId) =>
        _frames.Values.Where(f => f.ParentFrameId == parentFrameId).ToList<IFrame>();

    internal int? GetExecutionContextId(string frameId) =>
        _frameIdToExecutionContext.TryGetValue(frameId, out var id) ? id : null;

    internal async Task<T> EvaluateInFrameAsync<T>(string frameId, string expression, object? arg = null)
    {
        var contextId = GetExecutionContextId(frameId);

        var result = await _session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: WrapExpression(expression, arg),
                ReturnByValue: true,
                AwaitPromise: true,
                ContextId: contextId),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            CancellationToken.None).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Evaluation failed: {result.ExceptionDetails.Text}");

        return DeserializeRemoteObject<T>(result.Result);
    }

    internal async Task<T> WaitForFunctionInFrameAsync<T>(
        string frameId, string expression, object? arg, double? timeout)
    {
        var deadline = TimeSpan.FromMilliseconds(timeout ?? 30_000);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
        cts.CancelAfter(deadline);

        while (!cts.Token.IsCancellationRequested)
        {
            try
            {
                var result = await EvaluateInFrameAsync<JsonElement>(frameId, expression, arg).ConfigureAwait(false);

                if (IsTruthy(result))
                    return result.Deserialize<T>()!;
            }
            catch (InvalidOperationException)
            {
                // Evaluation failed, will retry
            }

            await Task.Delay(100, cts.Token).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"WaitForFunction timed out after {deadline.TotalMilliseconds}ms.");
    }

    internal async Task AddInitScriptInternalAsync(string script)
    {
        _initScripts.Add(script);
        await _session.SendAsync(
            "Page.addScriptToEvaluateOnNewDocument",
            new PageAddScriptToEvaluateOnNewDocumentParams(script),
            CdpJsonContext.Default.PageAddScriptToEvaluateOnNewDocumentParams,
            CdpJsonContext.Default.PageAddScriptToEvaluateOnNewDocumentResult,
            CancellationToken.None).ConfigureAwait(false);
    }

    internal async Task ExposeBindingInternalAsync(string name, Func<object?[], Task<object?>> callback)
    {
        _bindings[name] = callback;
        await _session.SendAsync(
            "Runtime.addBinding",
            new RuntimeAddBindingParams(name),
            CdpJsonContext.Default.RuntimeAddBindingParams,
            CdpJsonContext.Default.RuntimeAddBindingResult,
            CancellationToken.None).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_isClosed)
            return;

        _isClosed = true;

        if (_videoRecorder is not null)
        {
            try { await _videoRecorder.StopAndFinalizeAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
        }

        Close?.Invoke(this, EventArgs.Empty);
        _pageCts.Cancel();
        // Do not dispose _pageCts here; fire-and-forget handlers triggered by Close
        // may still reference the token (e.g. ScreencastService.StopAsync).
        // The CTS will be collected once all references are released.
    }

    private static string WrapExpression(string expression, object? arg)
    {
        if (arg is null)
            return expression;

        var serialized = JsonSerializer.Serialize(arg);
        return $"(({expression})({serialized}))";
    }

    private static T DeserializeRemoteObject<T>(RuntimeRemoteObject remoteObject)
    {
        if (remoteObject.Value is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Null)
                return default!;
            return element.Deserialize<T>()
                   ?? throw new InvalidOperationException("Deserialization returned null.");
        }

        if (remoteObject.Type == "undefined" || remoteObject.Subtype == "null")
            return default!;

        throw new InvalidOperationException(
            $"Cannot deserialize remote object of type '{remoteObject.Type}' to {typeof(T).Name}.");
    }

    private static bool IsTruthy(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => element.GetDouble() != 0,
            JsonValueKind.String => element.GetString()?.Length > 0,
            JsonValueKind.Object or JsonValueKind.Array => true,
            _ => false
        };
}
