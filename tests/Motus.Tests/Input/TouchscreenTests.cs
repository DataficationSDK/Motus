using Motus.Tests.Transport;

namespace Motus.Tests.Input;

[TestClass]
public class TouchscreenTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSession _session = null!;
    private Touchscreen _touchscreen = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var registry = new CdpSessionRegistry(_transport);
        _session = registry.CreateSession("test-session");
        _touchscreen = new Touchscreen(_session, CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task TapAsync_SendsTouchStartThenTouchEnd()
    {
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        _socket.QueueResponse("""{"id": 2, "sessionId": "test-session", "result": {}}""");
        await _touchscreen.TapAsync(30, 40);

        Assert.AreEqual(2, _socket.SentMessages.Count);
        Assert.IsTrue(_socket.GetSentJson(0).Contains("touchStart"));
        Assert.IsTrue(_socket.GetSentJson(1).Contains("touchEnd"));
    }
}
