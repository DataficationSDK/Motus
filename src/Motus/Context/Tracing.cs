using System.Text.Json;
using System.Threading.Channels;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Real ITracing implementation backed by CDP Tracing domain.
/// Uses the browser-level CDP session since Tracing is a browser-target domain.
/// </summary>
internal sealed class Tracing : ITracing
{
    private readonly CdpSession _browserSession;
    private readonly Channel<JsonElement[]> _dataChannel = Channel.CreateUnbounded<JsonElement[]>();
    private TaskCompletionSource<TracingTracingCompleteEvent>? _completeTcs;
    private CancellationTokenSource? _pumpCts;
    private int _started;

    internal Tracing(CdpSession browserSession)
    {
        _browserSession = browserSession;
    }

    /// <summary>
    /// Optional HarLog to include in the trace ZIP when stopping.
    /// Set by BrowserContext when HAR recording is active.
    /// </summary>
    internal HarLog? HarLog { get; set; }

    public async Task StartAsync(TracingStartOptions? options = null)
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0)
            throw new InvalidOperationException("Tracing is already started.");

        options ??= new TracingStartOptions();

        var categories = new List<string> { "devtools.timeline", "-*" };

        if (options.Screenshots == true)
            categories.Add("disabled-by-default-devtools.screenshot");

        if (options.Snapshots == true)
        {
            categories.Add("disabled-by-default-devtools.timeline.layers");
            categories.Add("disabled-by-default-devtools.timeline.picture");
        }

        // Drain any leftover data from a previous run
        while (_dataChannel.Reader.TryRead(out _)) { }

        _completeTcs = new TaskCompletionSource<TracingTracingCompleteEvent>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        _pumpCts = new CancellationTokenSource();

        // Start background pump for dataCollected events
        _ = PumpDataCollectedAsync(_pumpCts.Token);

        // Subscribe to tracingComplete
        _ = PumpTracingCompleteAsync(_pumpCts.Token);

        await _browserSession.SendAsync(
            "Tracing.start",
            new TracingStartParams(
                TransferMode: "ReturnAsStream",
                TraceConfig: new TracingTraceConfig(
                    IncludedCategories: categories.ToArray())),
            CdpJsonContext.Default.TracingStartParams,
            CdpJsonContext.Default.TracingStartResult,
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task StopAsync(TracingStopOptions? options = null)
    {
        if (Interlocked.CompareExchange(ref _started, 0, 1) != 1)
            return;

        var completeTcs = _completeTcs!;

        // Send Tracing.end
        await _browserSession.SendAsync(
            "Tracing.end",
            CdpJsonContext.Default.TracingEndResult,
            CancellationToken.None).ConfigureAwait(false);

        // Wait for tracingComplete event
        var completeEvent = await completeTcs.Task.ConfigureAwait(false);

        // Collect all accumulated data chunks
        var allEvents = new List<JsonElement>();
        _dataChannel.Writer.TryComplete();
        await foreach (var chunk in _dataChannel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            allEvents.AddRange(chunk);
        }

        // If a stream handle was returned, read the stream data
        if (completeEvent.Stream is not null)
        {
            var streamEvents = await ReadStreamAsync(completeEvent.Stream).ConfigureAwait(false);
            allEvents.AddRange(streamEvents);
        }

        // Cancel background pumps
        _pumpCts?.Cancel();

        // Extract screenshots from trace events
        var screenshots = ExtractScreenshots(allEvents);

        // Write trace ZIP if path specified
        if (options?.Path is not null)
        {
            await TracePackager.WriteAsync(
                options.Path,
                allEvents,
                HarLog,
                screenshots).ConfigureAwait(false);
        }

        // Reset for potential reuse
        HarLog = null;
    }

    private async Task PumpDataCollectedAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _browserSession.SubscribeAsync(
                "Tracing.dataCollected",
                CdpJsonContext.Default.TracingDataCollectedEvent,
                ct).ConfigureAwait(false))
            {
                await _dataChannel.Writer.WriteAsync(evt.Value, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task PumpTracingCompleteAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _browserSession.SubscribeAsync(
                "Tracing.tracingComplete",
                CdpJsonContext.Default.TracingTracingCompleteEvent,
                ct).ConfigureAwait(false))
            {
                _completeTcs?.TrySetResult(evt);
                break;
            }
        }
        catch (OperationCanceledException)
        {
            _completeTcs?.TrySetCanceled();
        }
    }

    private async Task<List<JsonElement>> ReadStreamAsync(string streamHandle)
    {
        var events = new List<JsonElement>();

        try
        {
            while (true)
            {
                var readResult = await _browserSession.SendAsync(
                    "IO.read",
                    new IoReadParams(streamHandle),
                    CdpJsonContext.Default.IoReadParams,
                    CdpJsonContext.Default.IoReadResult,
                    CancellationToken.None).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(readResult.Data))
                {
                    byte[] bytes;
                    if (readResult.Base64Encoded)
                        bytes = Convert.FromBase64String(readResult.Data);
                    else
                        bytes = System.Text.Encoding.UTF8.GetBytes(readResult.Data);

                    try
                    {
                        using var doc = JsonDocument.Parse(bytes);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var el in doc.RootElement.EnumerateArray())
                                events.Add(el.Clone());
                        }
                    }
                    catch (JsonException)
                    {
                        // Partial JSON chunk, store as raw string element
                    }
                }

                if (readResult.Eof)
                    break;
            }

            // Close the stream
            await _browserSession.SendAsync(
                "IO.close",
                new IoCloseParams(streamHandle),
                CdpJsonContext.Default.IoCloseParams,
                CdpJsonContext.Default.IoCloseResult,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Stream may already be closed
        }

        return events;
    }

    private static List<ScreenshotEntry> ExtractScreenshots(List<JsonElement> events)
    {
        var screenshots = new List<ScreenshotEntry>();
        int seq = 0;

        foreach (var evt in events)
        {
            if (evt.ValueKind != JsonValueKind.Object)
                continue;

            if (evt.TryGetProperty("cat", out var cat)
                && cat.GetString() == "disabled-by-default-devtools.screenshot"
                && evt.TryGetProperty("args", out var args)
                && args.TryGetProperty("snapshot", out var snapshot))
            {
                var base64 = snapshot.GetString();
                if (base64 is not null)
                {
                    screenshots.Add(new ScreenshotEntry(seq++, Convert.FromBase64String(base64)));
                }
            }
        }

        return screenshots;
    }
}
