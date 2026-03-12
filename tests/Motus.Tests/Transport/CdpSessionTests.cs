using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Motus.Tests.Transport;

// Test-only types to exercise CdpSession's typed serialization
internal sealed record TestParams(
    [property: JsonPropertyName("url")] string Url);

internal sealed record TestResponse(
    [property: JsonPropertyName("frameId")] string FrameId);

internal sealed record TestEvent(
    [property: JsonPropertyName("timestamp")] double Timestamp);

[JsonSerializable(typeof(TestParams))]
[JsonSerializable(typeof(TestResponse))]
[JsonSerializable(typeof(TestEvent))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class TestJsonContext : JsonSerializerContext;

[TestClass]
public class CdpSessionTests
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
    public async Task SendAsync_SerializesParamsAndDeserializesResponse()
    {
        var session = new CdpSession(_transport, sessionId: null);

        var sendTask = session.SendAsync(
            "Page.navigate",
            new TestParams("https://example.com"),
            TestJsonContext.Default.TestParams,
            TestJsonContext.Default.TestResponse,
            CancellationToken.None);

        _socket.Enqueue("""{"id":1,"result":{"frameId":"frame-1"}}""");

        var response = await sendTask;
        Assert.AreEqual("frame-1", response.FrameId);

        using var sentDoc = JsonDocument.Parse(_socket.GetSentJson(0));
        Assert.AreEqual("https://example.com", sentDoc.RootElement.GetProperty("params").GetProperty("url").GetString());
    }

    [TestMethod]
    public async Task SendAsync_NoParams_SendsEmptyObject()
    {
        var session = new CdpSession(_transport, sessionId: null);

        var sendTask = session.SendAsync(
            "Page.navigate",
            TestJsonContext.Default.TestResponse,
            CancellationToken.None);

        _socket.Enqueue("""{"id":1,"result":{"frameId":"frame-1"}}""");

        var response = await sendTask;
        Assert.AreEqual("frame-1", response.FrameId);
    }

    [TestMethod]
    public async Task SendAsync_FireAndForget_Completes()
    {
        var session = new CdpSession(_transport, sessionId: null);

        var sendTask = session.SendAsync(
            "Page.enable",
            new TestParams("https://example.com"),
            TestJsonContext.Default.TestParams,
            CancellationToken.None);

        _socket.Enqueue("""{"id":1,"result":{}}""");

        await sendTask;
        Assert.AreEqual(1, _socket.SentMessages.Count);
    }

    [TestMethod]
    public async Task SendAsync_WithSessionId_IncludesSessionIdInEnvelope()
    {
        var session = new CdpSession(_transport, sessionId: "page-session-1");

        var sendTask = session.SendAsync(
            "Page.navigate",
            new TestParams("https://example.com"),
            TestJsonContext.Default.TestParams,
            TestJsonContext.Default.TestResponse,
            CancellationToken.None);

        _socket.Enqueue("""{"id":1,"result":{"frameId":"frame-1"}}""");

        await sendTask;

        using var sentDoc = JsonDocument.Parse(_socket.GetSentJson(0));
        Assert.AreEqual("page-session-1", sentDoc.RootElement.GetProperty("sessionId").GetString());
    }

    [TestMethod]
    public async Task SubscribeAsync_DeserializesEvents()
    {
        var session = new CdpSession(_transport, sessionId: null);

        // Create the subscription FIRST so the channel exists before the event arrives
        await using var enumerator = session.SubscribeAsync(
            "Page.loadEventFired",
            TestJsonContext.Default.TestEvent,
            CancellationToken.None).GetAsyncEnumerator();

        // Now enqueue the event
        _socket.Enqueue("""{"method":"Page.loadEventFired","params":{"timestamp":100.5}}""");

        Assert.IsTrue(await enumerator.MoveNextAsync());
        Assert.AreEqual(100.5, enumerator.Current.Timestamp, 0.01);
    }

    [TestMethod]
    public async Task SubscribeAsync_SessionScoped_ReceivesOnlyOwnEvents()
    {
        var session1 = new CdpSession(_transport, sessionId: "s1");

        // Create subscription FIRST
        await using var enum1 = session1.SubscribeAsync(
            "Page.loadEventFired",
            TestJsonContext.Default.TestEvent,
            CancellationToken.None).GetAsyncEnumerator();

        // Enqueue event for session s1
        _socket.Enqueue("""{"method":"Page.loadEventFired","params":{"timestamp":1.0},"sessionId":"s1"}""");

        Assert.IsTrue(await enum1.MoveNextAsync());
        Assert.AreEqual(1.0, enum1.Current.Timestamp, 0.01);

        // Session2's channel should have no events (different composite key)
        var channel2Key = "Page.loadEventFired|s2";
        var channel2 = _transport.GetOrCreateEventChannel(channel2Key);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        bool received = false;
        try
        {
            await foreach (var _ in channel2.Reader.ReadAllAsync(cts.Token))
            {
                received = true;
                break;
            }
        }
        catch (OperationCanceledException) { }

        Assert.IsFalse(received);
    }
}
