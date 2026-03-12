using Motus.Tests.Transport;

namespace Motus.Tests.Selectors;

[TestClass]
public class TextSelectorStrategyTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
        _browser = new Motus.Browser(_transport, _registry, process: null, tempUserDataDir: null,
                                     handleSigint: false, handleSigterm: false);
        var initTask = _browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task ResolveAsync_PartialMatch_UsesIncludes()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": []}}""");

        var strategy = new TextSelectorStrategy();
        await strategy.ResolveAsync("Hello", ((Motus.Page)page).GetFrameForSelectors());

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("textContent") && s.Contains("includes")),
            "Partial match should use textContent.includes");
    }

    [TestMethod]
    public async Task ResolveAsync_ExactMatch_UsesStrictEquals()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": []}}""");

        var strategy = new TextSelectorStrategy();
        await strategy.ResolveAsync("\"Exact Text\"", ((Motus.Page)page).GetFrameForSelectors());

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("textContent") && s.Contains("trim()===")),
            "Exact match should use textContent.trim()===");
    }
}
