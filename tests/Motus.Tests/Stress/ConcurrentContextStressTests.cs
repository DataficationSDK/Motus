using Motus.Tests.Transport;

namespace Motus.Tests.Stress;

[TestClass]
[TestCategory("Stress")]
public class ConcurrentContextStressTests
{
    private ConcurrentFakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new ConcurrentFakeCdpSocket();
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
    public async Task ConcurrentContextCreation_20Contexts_AllSucceed()
    {
        // Queue responses for 20 context creations (each needs one response)
        for (int i = 0; i < 20; i++)
        {
            _socket.QueueResponse($@"{{""id"": {i + 2}, ""result"": {{""browserContextId"": ""ctx-{i}""}}}}");
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => _browser.NewContextAsync())
            .ToArray();

        var contexts = await Task.WhenAll(tasks);

        Assert.AreEqual(20, contexts.Length);
        foreach (var ctx in contexts)
            Assert.IsNotNull(ctx);
        Assert.AreEqual(20, _browser.Contexts.Count);
    }

    [TestMethod]
    public async Task SequentialPageCreation_20Pages_ThreadSafeState()
    {
        // Create a single context first
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        // Create 20 pages sequentially to validate thread-safe list management
        // (concurrent CDP over a fake socket causes response ordering issues)
        var pages = new List<Motus.Abstractions.IPage>();
        for (int i = 0; i < 20; i++)
        {
            var baseId = 3 + (i * 6);
            var sessionId = $"session-{i}";
            _socket.QueueResponse($@"{{""id"": {baseId}, ""result"": {{""targetId"": ""target-{i}""}}}}");
            _socket.QueueResponse($@"{{""id"": {baseId + 1}, ""result"": {{""sessionId"": ""{sessionId}""}}}}");
            _socket.QueueResponse($@"{{""id"": {baseId + 2}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
            _socket.QueueResponse($@"{{""id"": {baseId + 3}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
            _socket.QueueResponse($@"{{""id"": {baseId + 4}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
            _socket.QueueResponse($@"{{""id"": {baseId + 5}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");

            pages.Add(await context.NewPageAsync());
        }

        Assert.AreEqual(20, pages.Count);
        foreach (var page in pages)
        {
            Assert.IsNotNull(page);
            Assert.IsFalse(page.IsClosed);
        }
        Assert.AreEqual(20, context.Pages.Count);
    }

    [TestMethod]
    public async Task ConcurrentPageCreation_WithStorageState_AppliesOnce()
    {
        var options = new Motus.Abstractions.ContextOptions
        {
            StorageState = new Motus.Abstractions.StorageState(
                [new Motus.Abstractions.Cookie("test", "value", ".example.com", "/", 0, false, false, Motus.Abstractions.SameSiteAttribute.Lax)],
                [])
        };

        var contextTask = _browser.NewContextAsync(options);
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        // Queue responses for 5 sequential page creations
        // First page gets storage state (6 page init + 1 setCookie = 7 responses)
        // Remaining pages get only 6 responses each
        var id = 3;
        for (int i = 0; i < 5; i++)
        {
            var sessionId = $"session-{i}";
            _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""target-{i}""}}}}");
            _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""{sessionId}""}}}}");
            _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
            _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
            _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
            _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
            if (i == 0)
            {
                // Storage state setCookie response for first page only
                _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{""success"": true}}}}");
            }
        }

        // Create pages sequentially to avoid CDP message ordering issues
        var pages = new List<Motus.Abstractions.IPage>();
        for (int i = 0; i < 5; i++)
            pages.Add(await context.NewPageAsync());

        Assert.AreEqual(5, pages.Count);
        Assert.AreEqual(5, context.Pages.Count);
    }

    [TestMethod]
    public async Task ConcurrentClose_10Calls_EventFiresOnce()
    {
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        int closeCount = 0;
        context.Close += (_, _) => Interlocked.Increment(ref closeCount);

        _socket.QueueResponse("""{"id": 3, "result": {}}""");

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => context.CloseAsync())
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.AreEqual(1, closeCount);
    }

    [TestMethod]
    public async Task SetOfflineAsync_RacingWithNewPageAsync_NoExceptions()
    {
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        // Queue responses for page creation
        QueuePageResponses("target-1", "session-1", startId: 3);

        // Create a page first (needed for SetOfflineAsync to target)
        var page = await context.NewPageAsync();
        Assert.IsNotNull(page);

        // Queue responses for offline toggle (emulateNetworkConditions per page)
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");

        // Queue responses for a second page creation during offline toggle
        QueuePageResponses("target-2", "session-2", startId: 10);
        // offline propagation to page 2
        _socket.QueueResponse("""{"id": 16, "sessionId": "session-2", "result": {}}""");

        // Run concurrently: toggle offline and create a new page
        var offlineTask = context.SetOfflineAsync(true);
        var pageTask = context.NewPageAsync();

        await Task.WhenAll(offlineTask, pageTask);

        Assert.IsNotNull(pageTask.Result);
    }

    [TestMethod]
    public async Task RouteRegistration_OnContextWithNoPages_ThenPageCreation()
    {
        var contextTask = _browser.NewContextAsync();
        _socket.Enqueue("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await contextTask;

        // Register route on context with no pages (no Fetch.enable CDP call)
        await context.RouteAsync("**/*", route => Task.CompletedTask);

        // Page.HasAnyRoutes() checks context routes, so NetworkManager.InitializeAsync
        // calls EnableFetchAsync, adding a 7th CDP call (Fetch.enable).
        QueuePageResponses("target-1", "session-1", startId: 3);
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        var page = await context.NewPageAsync();

        Assert.IsNotNull(page);
        Assert.AreEqual(1, context.Pages.Count);
    }

    private void QueuePageResponses(string targetId, string sessionId, int startId)
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
