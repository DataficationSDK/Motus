using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Input;

[TestClass]
public class MouseTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private IMotusSession _session = null!;
    private Mouse _mouse = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var registry = new CdpSessionRegistry(_transport);
        _session = registry.CreateSession("test-session");
        _mouse = new Mouse(_session, CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task MoveAsync_SendsMouseMoved()
    {
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        await _mouse.MoveAsync(100, 200);

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("Input.dispatchMouseEvent"));
        Assert.IsTrue(sent.Contains("mouseMoved"));
    }

    [TestMethod]
    public async Task ClickAsync_SendsMovePressedReleased()
    {
        // move + pressed + released = 3 messages
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        _socket.QueueResponse("""{"id": 2, "sessionId": "test-session", "result": {}}""");
        _socket.QueueResponse("""{"id": 3, "sessionId": "test-session", "result": {}}""");
        await _mouse.ClickAsync(50, 75);

        Assert.AreEqual(3, _socket.SentMessages.Count);
        Assert.IsTrue(_socket.GetSentJson(0).Contains("mouseMoved"));
        Assert.IsTrue(_socket.GetSentJson(1).Contains("mousePressed"));
        Assert.IsTrue(_socket.GetSentJson(2).Contains("mouseReleased"));
    }

    [TestMethod]
    public async Task DblClickAsync_SendsCorrectClickCounts()
    {
        // move + 4 press/release events = 5 messages
        for (var i = 1; i <= 5; i++)
            _socket.QueueResponse($$$"""{"id": {{{i}}}, "sessionId": "test-session", "result": {}}""");
        await _mouse.DblClickAsync(10, 20);

        Assert.AreEqual(5, _socket.SentMessages.Count);
        Assert.IsTrue(_socket.GetSentJson(0).Contains("mouseMoved"));
        Assert.IsTrue(_socket.GetSentJson(1).Contains("mousePressed"));
        Assert.IsTrue(_socket.GetSentJson(1).Contains("\"clickCount\":1"));
        Assert.IsTrue(_socket.GetSentJson(3).Contains("mousePressed"));
        Assert.IsTrue(_socket.GetSentJson(3).Contains("\"clickCount\":2"));
    }

    [TestMethod]
    public async Task WheelAsync_SendsMouseWheelWithDeltas()
    {
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        await _mouse.WheelAsync(0, 100);

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("mouseWheel"));
        Assert.IsTrue(sent.Contains("\"deltaY\":100"));
    }

    [TestMethod]
    public async Task ClickAsync_WithModifiers_SendsModifierBitsOnEveryEvent()
    {
        // move + pressed + released = 3 messages
        for (var i = 1; i <= 3; i++)
            _socket.QueueResponse($$$"""{"id": {{{i}}}, "sessionId": "test-session", "result": {}}""");

        // Control (2) | Shift (8) = 10, matching the CDP modifier bits.
        await _mouse.ClickAsync(50, 75,
            new MouseButtonOptions(Modifiers: KeyModifier.Control | KeyModifier.Shift));

        Assert.AreEqual(3, _socket.SentMessages.Count);
        for (var i = 0; i < 3; i++)
            Assert.IsTrue(_socket.GetSentJson(i).Contains("\"modifiers\":10"),
                $"Message {i} should carry the modifier bits.");
    }

    [TestMethod]
    public async Task ClickAsync_WithoutModifiers_OmitsModifiersField()
    {
        for (var i = 1; i <= 3; i++)
            _socket.QueueResponse($$$"""{"id": {{{i}}}, "sessionId": "test-session", "result": {}}""");

        await _mouse.ClickAsync(50, 75);

        for (var i = 0; i < 3; i++)
            Assert.IsFalse(_socket.GetSentJson(i).Contains("modifiers"),
                $"Message {i} should not carry a modifiers field.");
    }

    [TestMethod]
    public async Task MoveAsync_WithModifiers_SendsModifierBits()
    {
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");

        await _mouse.MoveAsync(100, 200, new MouseMoveOptions(Modifiers: KeyModifier.Alt));

        Assert.IsTrue(_socket.GetSentJson(0).Contains("\"modifiers\":1"));
    }

    [TestMethod]
    public async Task DblClickAsync_WithModifiers_SendsModifierBitsOnEveryEvent()
    {
        for (var i = 1; i <= 5; i++)
            _socket.QueueResponse($$$"""{"id": {{{i}}}, "sessionId": "test-session", "result": {}}""");

        await _mouse.DblClickAsync(10, 20, new MouseButtonOptions(Modifiers: KeyModifier.Meta));

        Assert.AreEqual(5, _socket.SentMessages.Count);
        for (var i = 0; i < 5; i++)
            Assert.IsTrue(_socket.GetSentJson(i).Contains("\"modifiers\":4"),
                $"Message {i} should carry the modifier bits.");
    }
}
