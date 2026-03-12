using Motus.Tests.Transport;

namespace Motus.Tests.Input;

[TestClass]
public class KeyboardTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSession _session = null!;
    private Keyboard _keyboard = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var registry = new CdpSessionRegistry(_transport);
        _session = registry.CreateSession("test-session");
        _keyboard = new Keyboard(_session, CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task DownAsync_SendsRawKeyDown()
    {
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        await _keyboard.DownAsync("Enter");

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("Input.dispatchKeyEvent"));
        Assert.IsTrue(sent.Contains("rawKeyDown"));
        Assert.IsTrue(sent.Contains("Enter"));
    }

    [TestMethod]
    public async Task DownAsync_PrintableChar_SendsKeyDownAndChar()
    {
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        _socket.QueueResponse("""{"id": 2, "sessionId": "test-session", "result": {}}""");
        await _keyboard.DownAsync("a");

        Assert.AreEqual(2, _socket.SentMessages.Count);
        var first = _socket.GetSentJson(0);
        var second = _socket.GetSentJson(1);
        Assert.IsTrue(first.Contains("rawKeyDown"));
        Assert.IsTrue(second.Contains("\"type\":\"char\""));
    }

    [TestMethod]
    public async Task PressAsync_SendsDownAndUp()
    {
        // "Enter" press: rawKeyDown + keyUp = 2 messages
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        _socket.QueueResponse("""{"id": 2, "sessionId": "test-session", "result": {}}""");
        await _keyboard.PressAsync("Enter");

        Assert.AreEqual(2, _socket.SentMessages.Count);
        Assert.IsTrue(_socket.GetSentJson(0).Contains("rawKeyDown"));
        Assert.IsTrue(_socket.GetSentJson(1).Contains("keyUp"));
    }

    [TestMethod]
    public async Task PressAsync_CompoundKey_SendsModifiersAndKey()
    {
        // "Control+a": Control down(1), a down(2), a char(3), a up(4), Control up(5) = 5 messages
        for (var i = 1; i <= 5; i++)
            _socket.QueueResponse($$$"""{"id": {{{i}}}, "sessionId": "test-session", "result": {}}""");
        await _keyboard.PressAsync("Control+a");

        Assert.AreEqual(5, _socket.SentMessages.Count);
        Assert.IsTrue(_socket.GetSentJson(0).Contains("Control"));
        Assert.IsTrue(_socket.GetSentJson(0).Contains("rawKeyDown"));
        Assert.IsTrue(_socket.GetSentJson(4).Contains("Control"));
        Assert.IsTrue(_socket.GetSentJson(4).Contains("keyUp"));
    }

    [TestMethod]
    public async Task TypeAsync_IteratesCharacters()
    {
        // "hi" = 2 chars, each press = rawKeyDown + char + keyUp = 3 messages per char = 6 total
        for (var i = 1; i <= 6; i++)
            _socket.QueueResponse($$$"""{"id": {{{i}}}, "sessionId": "test-session", "result": {}}""");
        await _keyboard.TypeAsync("hi");

        Assert.AreEqual(6, _socket.SentMessages.Count);
    }

    [TestMethod]
    public async Task InsertTextAsync_SendsInsertText()
    {
        _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        await _keyboard.InsertTextAsync("hello world");

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("Input.insertText"));
        Assert.IsTrue(sent.Contains("hello world"));
    }
}
