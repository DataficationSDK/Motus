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

    [TestMethod]
    public async Task StopAsync_WithStreamHandle_ReadsStreamData()
    {
        var tracing = new Tracing(_browserSession);
        var tracePath = Path.Combine(Path.GetTempPath(), $"test-trace-stream-{Guid.NewGuid()}.zip");

        try
        {
            // Start
            _socket.QueueResponse("""{"id":1,"result":{}}""");
            await tracing.StartAsync();

            // Queue IO.read and IO.close responses before stop so they're ready
            // when the stream reading happens after tracingComplete
            _socket.QueueResponse("""{"id":2,"result":{}}"""); // Tracing.end
            _socket.QueueResponse("""{"id":3,"result":{"data":"[{\"cat\":\"devtools.timeline\",\"name\":\"test\"}]","base64Encoded":false,"eof":true}}"""); // IO.read
            _socket.QueueResponse("""{"id":4,"result":{}}"""); // IO.close

            var stopTask = tracing.StopAsync(new Motus.Abstractions.TracingStopOptions { Path = tracePath });

            // Simulate tracingComplete with stream handle
            await Task.Delay(100);
            _socket.Enqueue("""{"method":"Tracing.tracingComplete","params":{"dataLossOccurred":false,"stream":"stream-handle-1"}}""");

            await stopTask;

            Assert.IsTrue(File.Exists(tracePath), "Trace ZIP should be created");

            using var zip = ZipFile.OpenRead(tracePath);
            var traceJsonEntry = zip.GetEntry("trace.json");
            Assert.IsNotNull(traceJsonEntry);

            await using var stream = traceJsonEntry!.Open();
            var events = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream);
            Assert.IsNotNull(events);
            Assert.IsTrue(events!.Count > 0, "Should contain trace events from stream");
        }
        finally
        {
            if (File.Exists(tracePath))
                File.Delete(tracePath);
        }
    }

    [TestMethod]
    public async Task StartAsync_AlreadyStarted_ThrowsInvalidOperation()
    {
        var tracing = new Tracing(_browserSession);

        _socket.QueueResponse("""{"id":1,"result":{}}""");
        await tracing.StartAsync();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => tracing.StartAsync());
    }
}
