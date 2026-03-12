using Motus.Tests.Transport;

namespace Motus.Tests.Context;

[TestClass]
public class BrowserContextTests
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

        _browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        var initTask = _browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    [TestMethod]
    public async Task NewContextAsync_CreatesBrowserContext()
    {
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        Assert.IsNotNull(context);
        Assert.AreEqual(_browser, context.Browser);
        Assert.AreEqual(0, context.Pages.Count);
    }

    [TestMethod]
    public async Task NewContextAsync_AddsToContextsList()
    {
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        await contextTask;

        Assert.AreEqual(1, _browser.Contexts.Count);
    }

    [TestMethod]
    public async Task NewPageAsync_CreatesPageInNewContext()
    {
        QueuePageCreationResponses("ctx-1", "target-1", "session-1", startId: 2);

        var page = await _browser.NewPageAsync();

        Assert.IsNotNull(page);
        Assert.IsFalse(page.IsClosed);
        Assert.AreEqual(1, _browser.Contexts.Count);
    }

    [TestMethod]
    public async Task Context_NewPageAsync_FiresPageEvent()
    {
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        Motus.Abstractions.IPage? firedPage = null;
        context.Page += (_, p) => firedPage = p;

        QueuePageOnContextResponses("target-1", "session-1", startId: 3);
        var page = await context.NewPageAsync();

        Assert.IsNotNull(firedPage);
        Assert.AreEqual(page, firedPage);
    }

    [TestMethod]
    public async Task Context_CloseAsync_DisposesPages()
    {
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        QueuePageOnContextResponses("target-1", "session-1", startId: 3);
        var page = await context.NewPageAsync();

        // Queue the response for Target.disposeBrowserContext before calling close
        _socket.QueueResponse("""{"id": 9, "result": {}}""");
        await context.CloseAsync();

        Assert.IsTrue(page.IsClosed);
        Assert.AreEqual(0, context.Pages.Count);
    }

    private void QueuePageCreationResponses(string contextId, string targetId, string sessionId, int startId)
    {
        var id = startId;
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""browserContextId"": ""{contextId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""{targetId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""{sessionId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
    }

    private void QueuePageOnContextResponses(string targetId, string sessionId, int startId)
    {
        var id = startId;
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""{targetId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""{sessionId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
    }
}
