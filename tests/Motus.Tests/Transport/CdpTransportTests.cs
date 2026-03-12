using System.Text.Json;

namespace Motus.Tests.Transport;

[TestClass]
public class CdpTransportTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    [TestMethod]
    public async Task SendRawAsync_CorrelatesResponseById()
    {
        var emptyParams = CdpTransport.EmptyJsonElement();

        // Start the send (registers TCS in _pending), then enqueue the response
        var sendTask = _transport.SendRawAsync("Page.navigate", emptyParams, null, CancellationToken.None);
        _socket.Enqueue("""{"id":1,"result":{"frameId":"ABC"}}""");

        var result = await sendTask;
        Assert.AreEqual("ABC", result.GetProperty("frameId").GetString());
    }

    [TestMethod]
    public async Task SendRawAsync_ThrowsCdpProtocolException_WhenErrorReturned()
    {
        var emptyParams = CdpTransport.EmptyJsonElement();

        var sendTask = _transport.SendRawAsync("Page.navigate", emptyParams, null, CancellationToken.None);
        _socket.Enqueue("""{"id":1,"error":{"code":-32000,"message":"Page not found"}}""");

        var ex = await Assert.ThrowsExceptionAsync<CdpProtocolException>(() => sendTask);
        Assert.AreEqual(-32000, ex.Code);
        Assert.AreEqual("Page not found", ex.Message);
    }

    [TestMethod]
    public async Task SendRawAsync_IncludesSessionIdInEnvelope()
    {
        var emptyParams = CdpTransport.EmptyJsonElement();

        var sendTask = _transport.SendRawAsync("DOM.getDocument", emptyParams, "session-42", CancellationToken.None);
        _socket.Enqueue("""{"id":1,"result":{}}""");
        await sendTask;

        var sentJson = _socket.GetSentJson(0);
        using var doc = JsonDocument.Parse(sentJson);
        Assert.AreEqual("session-42", doc.RootElement.GetProperty("sessionId").GetString());
        Assert.AreEqual("DOM.getDocument", doc.RootElement.GetProperty("method").GetString());
    }

    [TestMethod]
    public async Task SendRawAsync_OmitsSessionIdWhenNull()
    {
        var emptyParams = CdpTransport.EmptyJsonElement();

        var sendTask = _transport.SendRawAsync("Target.getTargets", emptyParams, null, CancellationToken.None);
        _socket.Enqueue("""{"id":1,"result":{}}""");
        await sendTask;

        var sentJson = _socket.GetSentJson(0);
        using var doc = JsonDocument.Parse(sentJson);

        // sessionId should not be present (JsonIgnoreCondition.WhenWritingNull)
        Assert.IsFalse(doc.RootElement.TryGetProperty("sessionId", out _));
    }

    [TestMethod]
    public async Task SendRawAsync_UsesMonotonicallyIncreasingIds()
    {
        var emptyParams = CdpTransport.EmptyJsonElement();

        var send1 = _transport.SendRawAsync("method1", emptyParams, null, CancellationToken.None);
        _socket.Enqueue("""{"id":1,"result":{}}""");
        await send1;

        var send2 = _transport.SendRawAsync("method2", emptyParams, null, CancellationToken.None);
        _socket.Enqueue("""{"id":2,"result":{}}""");
        await send2;

        using var doc1 = JsonDocument.Parse(_socket.GetSentJson(0));
        using var doc2 = JsonDocument.Parse(_socket.GetSentJson(1));

        var id1 = doc1.RootElement.GetProperty("id").GetInt32();
        var id2 = doc2.RootElement.GetProperty("id").GetInt32();

        Assert.AreEqual(1, id1);
        Assert.AreEqual(2, id2);
    }

    [TestMethod]
    public async Task SubscribeAsync_DeliversMatchingEvents()
    {
        var channelKey = "Page.loadEventFired|";
        var channel = _transport.GetOrCreateEventChannel(channelKey);

        _socket.Enqueue("""{"method":"Page.loadEventFired","params":{"timestamp":123.45}}""");

        var raw = await ReadOneEvent(channel, TimeSpan.FromSeconds(5));
        Assert.AreEqual(123.45, raw.Params.GetProperty("timestamp").GetDouble(), 0.01);
    }

    [TestMethod]
    public async Task SubscribeAsync_DoesNotDeliverUnmatchedEvents()
    {
        var channelKey = "Page.loadEventFired|";
        var channel = _transport.GetOrCreateEventChannel(channelKey);

        // Send a different event
        _socket.Enqueue("""{"method":"Network.requestWillBeSent","params":{"requestId":"1"}}""");

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        bool received = false;
        try
        {
            await foreach (var _ in channel.Reader.ReadAllAsync(cts.Token))
            {
                received = true;
                break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.IsFalse(received);
    }

    [TestMethod]
    public async Task Disconnect_FaultsAllPendingSends()
    {
        var emptyParams = CdpTransport.EmptyJsonElement();
        var sendTask = _transport.SendRawAsync("Page.navigate", emptyParams, null, CancellationToken.None);

        // Give the send a moment to register the TCS
        await Task.Delay(50);
        _socket.SimulateDisconnect();

        await Assert.ThrowsExceptionAsync<CdpDisconnectedException>(() => sendTask);
    }

    [TestMethod]
    public async Task Disconnect_CompletesEventChannels()
    {
        var channelKey = "Page.loadEventFired|";
        var channel = _transport.GetOrCreateEventChannel(channelKey);

        _socket.SimulateDisconnect();

        // Wait for the receive loop to process the disconnect
        await Task.Delay(50);

        int count = 0;
        await foreach (var _ in channel.Reader.ReadAllAsync(CancellationToken.None))
        {
            count++;
        }

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public async Task Disconnect_RaisesDisconnectedEvent()
    {
        var disconnectedTcs = new TaskCompletionSource<Exception?>();
        _transport.Disconnected += ex => disconnectedTcs.TrySetResult(ex);

        _socket.SimulateDisconnect();

        var exception = await WithTimeout(disconnectedTcs.Task, TimeSpan.FromSeconds(5));
        Assert.IsNull(exception);
    }

    [TestMethod]
    public async Task SendRawAsync_ReturnsEmptyObjectWhenResultMissing()
    {
        var emptyParams = CdpTransport.EmptyJsonElement();

        var sendTask = _transport.SendRawAsync("Page.enable", emptyParams, null, CancellationToken.None);
        _socket.Enqueue("""{"id":1}""");

        var result = await sendTask;
        Assert.AreEqual(JsonValueKind.Object, result.ValueKind);
    }

    private static async Task<RawCdpEvent> ReadOneEvent(
        System.Threading.Channels.Channel<RawCdpEvent> channel, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        await foreach (var item in channel.Reader.ReadAllAsync(cts.Token))
        {
            return item;
        }
        throw new TimeoutException("No event received within timeout.");
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var completed = await Task.WhenAny(task, Task.Delay(Timeout.Infinite, cts.Token));
        if (completed != task)
            throw new TimeoutException("Task did not complete within timeout.");
        return await task;
    }
}
