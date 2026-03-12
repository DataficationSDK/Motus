using Motus.Tests.Transport;

namespace Motus.Tests.Handles;

[TestClass]
public class ElementHandleTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSession _session = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var registry = new CdpSessionRegistry(_transport);
        _session = registry.CreateSession("test-session");
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task GetAttributeAsync_ReturnsValue()
    {
        var handle = new ElementHandle(_session, "elem-1");

        _socket.QueueResponse("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "string", "value": "https://example.com" }
                }
            }
            """);

        var result = await handle.GetAttributeAsync("href");
        Assert.AreEqual("https://example.com", result);
    }

    [TestMethod]
    public async Task GetAttributeAsync_ReturnsNullForMissingAttribute()
    {
        var handle = new ElementHandle(_session, "elem-1");

        _socket.QueueResponse("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "object", "subtype": "null" }
                }
            }
            """);

        var result = await handle.GetAttributeAsync("data-foo");
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task TextContentAsync_ReturnsText()
    {
        var handle = new ElementHandle(_session, "elem-1");

        _socket.QueueResponse("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "string", "value": "Hello World" }
                }
            }
            """);

        var result = await handle.TextContentAsync();
        Assert.AreEqual("Hello World", result);
    }

    [TestMethod]
    public async Task BoundingBoxAsync_ReturnsBox()
    {
        var handle = new ElementHandle(_session, "elem-1");

        _socket.QueueResponse("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "object", "value": { "x": 10, "y": 20, "width": 100, "height": 50 } }
                }
            }
            """);

        var result = await handle.BoundingBoxAsync();
        Assert.IsNotNull(result);
        Assert.AreEqual(10, result.X);
        Assert.AreEqual(20, result.Y);
        Assert.AreEqual(100, result.Width);
        Assert.AreEqual(50, result.Height);
    }

    [TestMethod]
    public async Task BoundingBoxAsync_ReturnsNullForZeroSize()
    {
        var handle = new ElementHandle(_session, "elem-1");

        _socket.QueueResponse("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "object", "subtype": "null" }
                }
            }
            """);

        var result = await handle.BoundingBoxAsync();
        Assert.IsNull(result);
    }
}
