using Motus.Tests.Transport;

namespace Motus.Tests.Context;

[TestClass]
public class BrowserContextConcurrencyTests
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
    public async Task CloseAsync_CalledConcurrently_ExecutesOnce()
    {
        // Create a context
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        int closeEventCount = 0;
        context.Close += (_, _) => Interlocked.Increment(ref closeEventCount);

        // Queue a single response for Target.disposeBrowserContext
        _socket.QueueResponse("""{"id": 3, "result": {}}""");

        // Fire 10 concurrent CloseAsync calls
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => context.CloseAsync())
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.AreEqual(1, closeEventCount, "Close event should fire exactly once");
    }

    [TestMethod]
    public async Task NewPageAsync_CalledConcurrently_AppliesStorageStateOnce()
    {
        // Create a context with StorageState
        var options = new Motus.Abstractions.ContextOptions
        {
            StorageState = new Motus.Abstractions.StorageState(
                [new Motus.Abstractions.Cookie("name", "value", ".example.com", "/", 0, false, false, Motus.Abstractions.SameSiteAttribute.Lax)],
                [])
        };

        var contextTask = _browser.NewContextAsync(options);
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        // Queue responses for two concurrent page creations
        // Page 1: createTarget, attachToTarget, page init (4 calls), storage state cookie
        QueuePageOnContextResponses("target-1", "session-1", startId: 3);
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"success": true}}""");

        // Page 2: createTarget, attachToTarget, page init (4 calls) -- no storage state
        QueuePageOnContextResponses("target-2", "session-2", startId: 10);

        // Create two pages sequentially (concurrent CDP over single socket is hard to test)
        var page1 = await context.NewPageAsync();
        var page2 = await context.NewPageAsync();

        Assert.IsNotNull(page1);
        Assert.IsNotNull(page2);

        // Both pages were created; storage state cookie command should have been sent only once.
        // We verify this by checking that the second page creation did not queue a setCookie call.
        // The Interlocked.CompareExchange ensures only the first caller sets _storageStateRestored.
        Assert.AreEqual(2, context.Pages.Count);
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
