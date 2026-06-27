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

    [TestMethod]
    public async Task MoveAsync_WithNaturalMotion_SendsCurvedMultiStepPathEndingOnTarget()
    {
        // Distance 400 from the origin yields the capped 48 steps (400 / 8 = 50, clamped to 48),
        // each a single mouseMoved. The count is deterministic; only the path shape is randomized.
        var natural = new Mouse(_session, CancellationToken.None, naturalMotion: true);
        for (var i = 1; i <= 48; i++)
            _socket.QueueResponse($$$"""{"id": {{{i}}}, "sessionId": "test-session", "result": {}}""");

        await natural.MoveAsync(400, 0);

        Assert.AreEqual(48, _socket.SentMessages.Count);
        for (var i = 0; i < _socket.SentMessages.Count; i++)
            Assert.IsTrue(_socket.GetSentJson(i).Contains("mouseMoved"), $"Message {i} should be a move.");

        // The final event lands exactly on the target, not on a jittered intermediate point.
        var last = _socket.GetSentJson(_socket.SentMessages.Count - 1);
        Assert.IsTrue(last.Contains("\"x\":400") && last.Contains("\"y\":0"),
            $"The last move should land on the target: {last}");
    }

    [TestMethod]
    public async Task WheelAsync_WithNaturalMotion_SplitsIntoEasedStepsSummingToDelta()
    {
        // A 700px scroll yields clamp(700/40, 8, 30) = 17 eased wheel events whose deltas sum
        // exactly to the requested total, so the final scroll position is unchanged.
        var natural = new Mouse(_session, CancellationToken.None, naturalMotion: true);
        for (var i = 1; i <= 30; i++)
            _socket.QueueResponse($$$"""{"id": {{{i}}}, "sessionId": "test-session", "result": {}}""");

        await natural.WheelAsync(0, 700);

        Assert.AreEqual(17, _socket.SentMessages.Count);

        double sum = 0;
        for (var i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            Assert.IsTrue(json.Contains("mouseWheel"), $"Message {i} should be a wheel event.");
            var match = System.Text.RegularExpressions.Regex.Match(json, "\"deltaY\":(-?[0-9.]+)");
            Assert.IsTrue(match.Success, $"Message {i} should carry deltaY: {json}");
            sum += double.Parse(match.Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        Assert.AreEqual(700.0, sum, 0.0001, "The eased increments should sum to the requested delta.");
    }

    [TestMethod]
    public async Task WheelAsync_WithNaturalMotion_SmallScroll_SendsSingleEvent()
    {
        // Small nudges stay a single wheel event even with natural motion enabled.
        var natural = new Mouse(_session, CancellationToken.None, naturalMotion: true);
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");

        await natural.WheelAsync(0, 40);

        Assert.AreEqual(1, _socket.SentMessages.Count);
        Assert.IsTrue(_socket.GetSentJson(0).Contains("\"deltaY\":40"));
    }

    [TestMethod]
    public async Task MoveAsync_WithNaturalMotion_ShortHop_SendsSingleEvent()
    {
        // Moves under the small-distance threshold collapse to one event at the target.
        var natural = new Mouse(_session, CancellationToken.None, naturalMotion: true);
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");

        await natural.MoveAsync(2, 2);

        Assert.AreEqual(1, _socket.SentMessages.Count);
        var only = _socket.GetSentJson(0);
        Assert.IsTrue(only.Contains("mouseMoved"));
        Assert.IsTrue(only.Contains("\"x\":2") && only.Contains("\"y\":2"), only);
    }
}
