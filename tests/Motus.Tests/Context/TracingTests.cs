using System.IO.Compression;
using System.Text.Json;
using Motus.Tests.Transport;

namespace Motus.Tests.Context;

[TestClass]
public class TracingTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSession _browserSession = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);
        _browserSession = new CdpSession(_transport, sessionId: null);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    [TestMethod]
    public async Task StartAsync_SendsTracingStartCommand()
    {
        var tracing = new Tracing(_browserSession);

        // Queue response for Tracing.start
        _socket.QueueResponse("""{"id":1,"result":{}}""");

        await tracing.StartAsync(new Motus.Abstractions.TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Name = "test-trace"
        });

        // Verify the Tracing.start was sent
        Assert.IsTrue(_socket.SentMessages.Count >= 1);
        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("Tracing.start"), "Should send Tracing.start command");
    }

    [TestMethod]
    public async Task StopAsync_WithPath_WritesZipFile()
    {
        var tracing = new Tracing(_browserSession);
        var tracePath = Path.Combine(Path.GetTempPath(), $"test-trace-{Guid.NewGuid()}.zip");

        try
        {
            // Start tracing
            _socket.QueueResponse("""{"id":1,"result":{}}""");
            await tracing.StartAsync();

            // Stop: queue Tracing.end result, then fire tracingComplete event
            _socket.QueueResponse("""{"id":2,"result":{}}""");

            var stopTask = tracing.StopAsync(new Motus.Abstractions.TracingStopOptions { Path = tracePath });

            // Simulate tracingComplete event (no stream handle)
            await Task.Delay(50);
            _socket.Enqueue("""{"method":"Tracing.tracingComplete","params":{"dataLossOccurred":false}}""");

            await stopTask;

            Assert.IsTrue(File.Exists(tracePath), "Trace ZIP should be created");
            Assert.IsTrue(new FileInfo(tracePath).Length > 0, "Trace ZIP should be non-empty");

            // Verify it's a valid ZIP containing trace.json
            using var zip = ZipFile.OpenRead(tracePath);
            var traceJsonEntry = zip.GetEntry("trace.json");
            Assert.IsNotNull(traceJsonEntry, "ZIP should contain trace.json");
        }
        finally
        {
            if (File.Exists(tracePath))
                File.Delete(tracePath);
        }
    }

    /// <summary>
    /// The shape the browser actually streams: the events sit inside an object, with a
    /// metadata sibling alongside them.
    /// </summary>
    /// <remarks>
    /// Reading the payload as if its root were the event array produced a trace file
    /// holding an empty list, which every caller downstream accepted as a valid trace.
    /// </remarks>
    [TestMethod]
    public async Task StopAsync_WithWrappedStreamPayload_KeepsEveryEvent()
    {
        var payload = """
            {"traceEvents":[
            {"cat":"devtools.timeline","name":"ParseHTML","ts":100,"dur":5},
            {"cat":"blink,devtools.timeline","name":"EventDispatch","ts":200,"dur":3},
            {"cat":"devtools.timeline","name":"Paint","ts":300,"dur":1}
            ],"metadata":{"clock-domain":"MAC_MACH_ABSOLUTE_TIME"}}
            """;

        var events = await CaptureStreamedTraceAsync([payload]);

        Assert.AreEqual(3, events.Count, "Every streamed event should reach trace.json.");
        Assert.AreEqual("ParseHTML", events[0].GetProperty("name").GetString());
        Assert.AreEqual("EventDispatch", events[1].GetProperty("name").GetString());
        Assert.AreEqual("Paint", events[2].GetProperty("name").GetString());
    }

    /// <summary>
    /// The browser decides where the chunk boundaries fall, and a boundary can land in the
    /// middle of a JSON token, so no single chunk is parseable on its own.
    /// </summary>
    [TestMethod]
    public async Task StopAsync_WithStreamPayloadSplitAcrossChunks_KeepsEveryEvent()
    {
        var payload = """{"traceEvents":[{"cat":"devtools.timeline","name":"ParseHTML","ts":100},{"cat":"devtools.timeline","name":"Layout","ts":200}],"metadata":{}}""";

        // Split mid-token so that no chunk parses by itself.
        var chunks = new[]
        {
            payload[..20],
            payload[20..55],
            payload[55..],
        };

        var events = await CaptureStreamedTraceAsync(chunks);

        Assert.AreEqual(2, events.Count, "Chunks must be joined before the payload is parsed.");
        Assert.AreEqual("ParseHTML", events[0].GetProperty("name").GetString());
        Assert.AreEqual("Layout", events[1].GetProperty("name").GetString());
    }

    /// <summary>
    /// A bare array is what the event-reporting transfer mode produces, so it stays supported.
    /// </summary>
    [TestMethod]
    public async Task StopAsync_WithBareArrayStreamPayload_KeepsEveryEvent()
    {
        var events = await CaptureStreamedTraceAsync(
            ["""[{"cat":"devtools.timeline","name":"Paint","ts":10}]"""]);

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual("Paint", events[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// When the browser reports gzip compression on the stream, the bytes arrive base64 encoded
    /// and have to be inflated before anything can be read out of them.
    /// </summary>
    [TestMethod]
    public async Task StopAsync_WithGzippedStreamPayload_KeepsEveryEvent()
    {
        var payload = """{"traceEvents":[{"cat":"devtools.timeline","name":"Layout","ts":42}]}""";

        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var raw = System.Text.Encoding.UTF8.GetBytes(payload);
            gzip.Write(raw, 0, raw.Length);
        }

        var events = await CaptureStreamedTraceAsync(
            [Convert.ToBase64String(compressed.ToArray())],
            base64Encoded: true,
            streamCompression: "gzip");

        Assert.AreEqual(1, events.Count);
        Assert.AreEqual("Layout", events[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// A payload that cannot be read has to fail the stop. Writing the file anyway hands the
    /// caller a trace that opens cleanly and plays back as nothing.
    /// </summary>
    [TestMethod]
    public async Task StopAsync_WithUnreadableStreamPayload_ThrowsInsteadOfWritingAnEmptyTrace()
    {
        var tracing = new Tracing(_browserSession);
        var tracePath = Path.Combine(Path.GetTempPath(), $"test-trace-bad-{Guid.NewGuid()}.zip");

        try
        {
            _socket.QueueResponse("""{"id":1,"result":{}}""");
            await tracing.StartAsync();

            _socket.QueueResponse("""{"id":2,"result":{}}"""); // Tracing.end
            _socket.QueueResponse(IoReadResponse("""{"traceEvents":[{"cat":"devtool""", eof: true));
            _socket.QueueResponse("""{"id":4,"result":{}}"""); // IO.close

            var stopTask = tracing.StopAsync(new Motus.Abstractions.TracingStopOptions { Path = tracePath });

            await Task.Delay(100);
            _socket.Enqueue("""{"method":"Tracing.tracingComplete","params":{"dataLossOccurred":false,"stream":"stream-handle-1"}}""");

            await Assert.ThrowsExceptionAsync<Motus.Abstractions.MotusProtocolException>(() => stopTask);

            Assert.IsFalse(File.Exists(tracePath),
                "A trace that could not be read should not be written to disk at all.");
        }
        finally
        {
            if (File.Exists(tracePath))
                File.Delete(tracePath);
        }
    }

    /// <summary>
    /// Runs a full start/stop cycle whose tracingComplete carries a stream handle, feeding the
    /// given chunks back through IO.read, and returns what landed in trace.json.
    /// </summary>
    private async Task<List<JsonElement>> CaptureStreamedTraceAsync(
        string[] chunks,
        bool base64Encoded = false,
        string? streamCompression = null)
    {
        var tracing = new Tracing(_browserSession);
        var tracePath = Path.Combine(Path.GetTempPath(), $"test-trace-stream-{Guid.NewGuid()}.zip");

        try
        {
            _socket.QueueResponse("""{"id":1,"result":{}}""");
            await tracing.StartAsync();

            // Queued before the stop so each response is ready for its outbound command:
            // Tracing.end, then one IO.read per chunk, then IO.close.
            _socket.QueueResponse("""{"id":2,"result":{}}""");
            for (int i = 0; i < chunks.Length; i++)
                _socket.QueueResponse(IoReadResponse(chunks[i], eof: i == chunks.Length - 1, base64Encoded));
            _socket.QueueResponse("""{"id":99,"result":{}}""");

            var stopTask = tracing.StopAsync(new Motus.Abstractions.TracingStopOptions { Path = tracePath });

            await Task.Delay(100);
            var compression = streamCompression is null ? "" : $@",""streamCompression"":""{streamCompression}""";
            _socket.Enqueue(
                $@"{{""method"":""Tracing.tracingComplete"",""params"":{{""dataLossOccurred"":false,""stream"":""stream-handle-1"",""traceFormat"":""json""{compression}}}}}");

            await stopTask;

            Assert.IsTrue(File.Exists(tracePath), "Trace ZIP should be created");

            using var zip = ZipFile.OpenRead(tracePath);
            var traceJsonEntry = zip.GetEntry("trace.json");
            Assert.IsNotNull(traceJsonEntry, "ZIP should contain trace.json");

            await using var stream = traceJsonEntry!.Open();
            var events = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream);
            Assert.IsNotNull(events);
            return events!;
        }
        finally
        {
            if (File.Exists(tracePath))
                File.Delete(tracePath);
        }
    }

    private static string IoReadResponse(string data, bool eof, bool base64Encoded = false)
        => $@"{{""id"":0,""result"":{{""data"":{JsonSerializer.Serialize(data)},""base64Encoded"":{(base64Encoded ? "true" : "false")},""eof"":{(eof ? "true" : "false")}}}}}";

    [TestMethod]
    public async Task StartAsync_AlreadyStarted_ThrowsInvalidOperation()
    {
        var tracing = new Tracing(_browserSession);

        _socket.QueueResponse("""{"id":1,"result":{}}""");
        await tracing.StartAsync();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => tracing.StartAsync());
    }

    [TestMethod]
    public async Task StartAsync_ConcurrentOnSameBrowser_SerializesViaGate()
    {
        // Two Tracing instances backed by the same browser session — mirrors what
        // happens when MotusTestBase shares a single Chrome process across parallel
        // tests. CDP Tracing is browser-wide, so the second StartAsync must wait
        // until the first StopAsync releases the gate.
        var first = new Tracing(_browserSession);
        var second = new Tracing(_browserSession);

        _socket.QueueResponse("""{"id":1,"result":{}}"""); // first start

        await first.StartAsync();

        var secondStartTask = second.StartAsync();
        // Give the second start time to advance to its WaitAsync; if the gate did not
        // exist it would already have sent its Tracing.start command.
        await Task.Delay(50);
        Assert.IsFalse(secondStartTask.IsCompleted, "Second StartAsync should be gated until the first stops.");

        // Now stop the first and let the second proceed.
        _socket.QueueResponse("""{"id":2,"result":{}}"""); // first end
        var firstStopTask = first.StopAsync();
        await Task.Delay(50);
        _socket.Enqueue("""{"method":"Tracing.tracingComplete","params":{"dataLossOccurred":false}}""");
        await firstStopTask;

        _socket.QueueResponse("""{"id":3,"result":{}}"""); // second start (now unblocked)
        await secondStartTask;

        // Cleanly stop the second so the gate is released for any teardown.
        _socket.QueueResponse("""{"id":4,"result":{}}""");
        var secondStopTask = second.StopAsync();
        await Task.Delay(50);
        _socket.Enqueue("""{"method":"Tracing.tracingComplete","params":{"dataLossOccurred":false}}""");
        await secondStopTask;
    }

    [TestMethod]
    public async Task ReleaseStateOnContextClose_FreesGate_WhenStopAsyncWasNeverCalled()
    {
        // Models the real-world failure mode: a test calls StartAsync but exits
        // before reaching StopAsync (early return, untracked exception path).
        // BrowserContext.CloseAsync invokes ReleaseStateOnContextClose on disposal.
        // Without that call, the gate would stay held for the browser session's
        // lifetime and any subsequent Tracing.StartAsync would deadlock.
        var leaker = new Tracing(_browserSession);
        var nextTest = new Tracing(_browserSession);

        _socket.QueueResponse("""{"id":1,"result":{}}"""); // leaker's start
        await leaker.StartAsync();

        // Leaker's BrowserContext is being disposed. Test author forgot StopAsync.
        leaker.ReleaseStateOnContextClose();

        // The next test on the same browser must be able to start tracing —
        // otherwise the leak permanently deadlocks the run.
        _socket.QueueResponse("""{"id":2,"result":{}}"""); // next test's start
        var nextStartTask = nextTest.StartAsync();

        // Should complete promptly, not block on the leaked gate.
        var completedFirst = await Task.WhenAny(nextStartTask, Task.Delay(2000));
        Assert.AreSame(nextStartTask, completedFirst,
            "Next Tracing.StartAsync hung — ReleaseStateOnContextClose did not free the gate.");
        await nextStartTask;

        // Cleanly stop the next so the gate is released for teardown.
        _socket.QueueResponse("""{"id":3,"result":{}}""");
        var stopTask = nextTest.StopAsync();
        await Task.Delay(50);
        _socket.Enqueue("""{"method":"Tracing.tracingComplete","params":{"dataLossOccurred":false}}""");
        await stopTask;
    }
}
