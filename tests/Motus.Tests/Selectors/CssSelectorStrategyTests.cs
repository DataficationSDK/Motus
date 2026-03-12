using Motus.Tests.Transport;

namespace Motus.Tests.Selectors;

[TestClass]
public class CssSelectorStrategyTests
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
    public async Task ResolveAsync_SendsQuerySelectorAll()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        // Queue response for querySelectorAll eval (returns empty array object)
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        // Queue response for getProperties (empty result)
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": []}}""");

        var strategy = new CssSelectorStrategy();
        var handles = await strategy.ResolveAsync("div.test", ((Motus.Page)page).GetFrameForSelectors());

        Assert.AreEqual(0, handles.Count);

        // Verify querySelectorAll was in the sent JS
        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("querySelectorAll") && s.Contains("div.test")),
            "Should send querySelectorAll with the CSS selector");
    }
}
