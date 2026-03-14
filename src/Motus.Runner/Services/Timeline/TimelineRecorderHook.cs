using System.Diagnostics;
using Motus.Abstractions;

namespace Motus.Runner.Services.Timeline;

internal sealed class TimelineRecorderHook : ILifecycleHook
{
    private readonly ITimelineService _timeline;
    private readonly IStepDebugService _stepDebug;

    private byte[]? _screenshotBefore;
    private Stopwatch? _stopwatch;
    private readonly List<NetworkCapture> _pendingNetwork = [];
    private readonly List<ConsoleCapture> _pendingConsole = [];

    public TimelineRecorderHook(ITimelineService timeline, IStepDebugService stepDebug)
    {
        _timeline = timeline;
        _stepDebug = stepDebug;
    }

    public async Task BeforeActionAsync(IPage page, string action)
    {
        var selector = ActionContext.CurrentSelector.Value;

        lock (_pendingNetwork) _pendingNetwork.Clear();
        lock (_pendingConsole) _pendingConsole.Clear();

        try
        {
            _screenshotBefore = await page.ScreenshotAsync().ConfigureAwait(false);
        }
        catch
        {
            _screenshotBefore = null;
        }

        _stopwatch = Stopwatch.StartNew();

        await _stepDebug.WaitIfPausedAsync(action, selector, CancellationToken.None).ConfigureAwait(false);
    }

    public async Task AfterActionAsync(IPage page, string action, ActionResult result)
    {
        _stopwatch?.Stop();
        var duration = _stopwatch?.Elapsed ?? TimeSpan.Zero;
        var selector = ActionContext.CurrentSelector.Value;

        byte[]? screenshotAfter = null;
        try
        {
            screenshotAfter = await page.ScreenshotAsync().ConfigureAwait(false);
        }
        catch
        {
            // Page may be navigating or closed
        }

        List<NetworkCapture> network;
        List<ConsoleCapture> console;
        lock (_pendingNetwork)
            network = [.. _pendingNetwork];
        lock (_pendingConsole)
            console = [.. _pendingConsole];

        var entries = _timeline.Entries;
        var entry = new TimelineEntry(
            Index: entries.Count,
            Timestamp: DateTime.UtcNow,
            ActionType: action,
            Selector: selector,
            Duration: duration,
            ScreenshotBefore: _screenshotBefore,
            ScreenshotAfter: screenshotAfter,
            HasError: result.Error is not null,
            ErrorMessage: result.Error?.Message,
            NetworkRequests: network,
            ConsoleMessages: console);

        _timeline.AddEntry(entry);
        _screenshotBefore = null;
        _stopwatch = null;
    }

    public Task OnPageCreatedAsync(IPage page)
    {
        page.Request += OnRequest;
        page.RequestFinished += OnRequestFinished;
        page.RequestFailed += OnRequestFailed;
        page.Console += OnConsole;
        return Task.CompletedTask;
    }

    public Task OnPageClosedAsync(IPage page)
    {
        page.Request -= OnRequest;
        page.RequestFinished -= OnRequestFinished;
        page.RequestFailed -= OnRequestFailed;
        page.Console -= OnConsole;
        return Task.CompletedTask;
    }

    public async Task BeforeNavigationAsync(IPage page, string url)
    {
        lock (_pendingNetwork) _pendingNetwork.Clear();
        lock (_pendingConsole) _pendingConsole.Clear();

        try
        {
            _screenshotBefore = await page.ScreenshotAsync().ConfigureAwait(false);
        }
        catch
        {
            _screenshotBefore = null;
        }

        _stopwatch = Stopwatch.StartNew();
    }

    public async Task AfterNavigationAsync(IPage page, IResponse? response)
    {
        _stopwatch?.Stop();
        var duration = _stopwatch?.Elapsed ?? TimeSpan.Zero;

        byte[]? screenshotAfter = null;
        try
        {
            screenshotAfter = await page.ScreenshotAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort
        }

        List<NetworkCapture> network;
        List<ConsoleCapture> console;
        lock (_pendingNetwork)
            network = [.. _pendingNetwork];
        lock (_pendingConsole)
            console = [.. _pendingConsole];

        var entries = _timeline.Entries;
        var entry = new TimelineEntry(
            Index: entries.Count,
            Timestamp: DateTime.UtcNow,
            ActionType: "navigate",
            Selector: page.Url,
            Duration: duration,
            ScreenshotBefore: _screenshotBefore,
            ScreenshotAfter: screenshotAfter,
            HasError: response is not null && !response.Ok,
            ErrorMessage: response is not null && !response.Ok ? $"HTTP {response.Status}" : null,
            NetworkRequests: network,
            ConsoleMessages: console);

        _timeline.AddEntry(entry);
        _screenshotBefore = null;
        _stopwatch = null;
    }

    public Task OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message) => Task.CompletedTask;

    public Task OnPageErrorAsync(IPage page, PageErrorEventArgs error) => Task.CompletedTask;

    private void OnRequest(object? sender, RequestEventArgs e)
    {
        // Captured in pending; final status recorded on finish/fail
    }

    private void OnRequestFinished(object? sender, RequestEventArgs e)
    {
        var req = e.Request;
        var status = req.Response?.Status ?? 0;
        var capture = new NetworkCapture(req.Url, req.Method, status, !req.Response?.Ok ?? false);
        lock (_pendingNetwork)
            _pendingNetwork.Add(capture);
    }

    private void OnRequestFailed(object? sender, RequestEventArgs e)
    {
        var req = e.Request;
        var capture = new NetworkCapture(req.Url, req.Method, 0, Failed: true);
        lock (_pendingNetwork)
            _pendingNetwork.Add(capture);
    }

    private void OnConsole(object? sender, ConsoleMessageEventArgs e)
    {
        var capture = new ConsoleCapture(e.Type, e.Text);
        lock (_pendingConsole)
            _pendingConsole.Add(capture);
    }
}
