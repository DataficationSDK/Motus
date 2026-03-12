using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Locator;

[TestClass]
public class ActionabilityTests
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

    private async Task<IPage> CreatePageAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        return await _browser.NewPageAsync();
    }

    /// <summary>
    /// Queues 2 CDP responses for strategy-based element resolution:
    /// 1. Runtime.evaluate (querySelectorAll returning array objectId)
    /// 2. Runtime.getProperties (returning element descriptors)
    /// </summary>
    private void QueueStrategyResolve(ref int id, string objectId)
    {
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-{objectId}""}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""0"", ""value"": {{""type"": ""object"", ""objectId"": ""{objectId}""}}}}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 1}}}}]}}}}");
    }

    [TestMethod]
    public async Task VisibilityCheck_PassesWhenVisible()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        var id = 8;
        // resolve (strategy: 2 calls)
        QueueStrategyResolve(ref id, "btn-1");
        // visible check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // stable check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // receives-events: bounding box
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 10, ""y"": 20, ""width"": 100, ""height"": 50}}}}}}}}");
        // DOM.getNodeForLocation
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""backendNodeId"": 7}}}}");
        // DOM.resolveNode
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""object"": {{""type"": ""object"", ""objectId"": ""resolved-7""}}}}}}");
        // identity check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // GetBoundingBoxOrThrow for click
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 10, ""y"": 20, ""width"": 100, ""height"": 50}}}}}}}}");
        // Mouse events
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        await locator.ClickAsync();
    }

    [TestMethod]
    public async Task EnabledCheck_PassesWhenEnabled()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("input#name");

        var id = 8;
        // resolve (strategy: 2 calls)
        QueueStrategyResolve(ref id, "input-1");
        // visible check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // editable check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // EvalOnElementVoidAsync for fill action
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""undefined""}}}}}}");

        await locator.FillAsync("test value");
    }

    [TestMethod]
    public async Task TimeoutThrows_WhenElementNeverAppears()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#never-exists");

        // Strategy resolve returns empty arrays repeatedly
        for (int i = 0; i < 40; i += 2)
        {
            _socket.QueueResponse($@"{{""id"": {8 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-empty-{i}""}}}}}}");
            _socket.QueueResponse($@"{{""id"": {9 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": []}}}}");
        }

        await Assert.ThrowsExceptionAsync<TimeoutException>(
            () => locator.ClickAsync(timeout: 200));
    }

    [TestMethod]
    public async Task StabilityCheck_SendsRafJs()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#anim");

        var id = 8;
        QueueStrategyResolve(ref id, "anim-1");
        // visible -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // stable check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // receives-events: bounding box
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 0, ""y"": 0, ""width"": 50, ""height"": 50}}}}}}}}");
        // DOM.getNodeForLocation
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""backendNodeId"": 5}}}}");
        // DOM.resolveNode
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""object"": {{""type"": ""object"", ""objectId"": ""resolved-5""}}}}}}");
        // identity check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // bounding box for click
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 0, ""y"": 0, ""width"": 50, ""height"": 50}}}}}}}}");
        // mouse events
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        await locator.ClickAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("requestAnimationFrame")),
            "Stability check should include requestAnimationFrame in the JS function.");
    }

    [TestMethod]
    public async Task ReceivesEventsCheck_SendsDomGetNodeForLocation()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#overlay-target");

        var id = 8;
        QueueStrategyResolve(ref id, "ot-1");
        // visible -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // stable -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // receives-events: bounding box
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 50, ""y"": 50, ""width"": 100, ""height"": 40}}}}}}}}");
        // DOM.getNodeForLocation
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""backendNodeId"": 99}}}}");
        // DOM.resolveNode
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""object"": {{""type"": ""object"", ""objectId"": ""hit-99""}}}}}}");
        // identity check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // bounding box for click
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 50, ""y"": 50, ""width"": 100, ""height"": 40}}}}}}}}");
        // mouse events
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        await locator.ClickAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("DOM.getNodeForLocation")),
            "Receives-events check should send DOM.getNodeForLocation.");
        Assert.IsTrue(allSent.Any(s => s.Contains("DOM.resolveNode")),
            "Receives-events check should send DOM.resolveNode.");
    }

    [TestMethod]
    public async Task HoverAsync_UsesVisibleStableReceivesEventsFlags()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#hoverable");

        var id = 8;
        QueueStrategyResolve(ref id, "hov-1");
        // visible -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // stable -> true (no enabled check for hover)
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // receives-events: bounding box
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 0, ""y"": 0, ""width"": 60, ""height"": 30}}}}}}}}");
        // DOM.getNodeForLocation
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""backendNodeId"": 3}}}}");
        // DOM.resolveNode
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""object"": {{""type"": ""object"", ""objectId"": ""resolved-3""}}}}}}");
        // identity check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // bounding box for hover
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 0, ""y"": 0, ""width"": 60, ""height"": 30}}}}}}}}");
        // mouseMoved
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        await locator.HoverAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("mouseMoved")));
    }

    [TestMethod]
    public async Task FocusAsync_UsesNoActionabilityFlags()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#focusable");

        var id = 8;
        // resolve (strategy: 2 calls)
        QueueStrategyResolve(ref id, "f-1");
        // focus eval
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""undefined""}}}}}}");

        await locator.FocusAsync();
    }
}
