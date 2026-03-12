using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Locator;

[TestClass]
public class LocatorTests
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
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        return await _browser.NewPageAsync();
    }

    /// <summary>
    /// Queues 2 CDP responses for strategy-based single element resolution:
    /// 1. Runtime.evaluate (querySelectorAll returning array objectId)
    /// 2. Runtime.getProperties (returning element descriptors)
    /// </summary>
    private void QueueStrategyResolve(ref int id, string objectId = "btn-1")
    {
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-{objectId}""}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""0"", ""value"": {{""type"": ""object"", ""objectId"": ""{objectId}""}}}}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 1}}}}]}}}}");
    }

    /// <summary>
    /// Queues the CDP responses needed for actionability checks on a click action:
    /// 1-2. resolve (strategy: Runtime.evaluate + Runtime.getProperties)
    /// 3. visible check (Runtime.callFunctionOn)
    /// 4. enabled check (Runtime.callFunctionOn)
    /// 5. stable check (Runtime.callFunctionOn)
    /// 6. receives-events: bounding box (Runtime.callFunctionOn)
    /// 7. receives-events: DOM.getNodeForLocation
    /// 8. receives-events: DOM.resolveNode
    /// 9. receives-events: identity check (Runtime.callFunctionOn)
    /// Then the action-specific calls follow.
    /// </summary>
    private void QueueClickActionabilityResponses(int startId, string objectId = "btn-1")
    {
        var id = startId;
        // Strategy resolve: evaluate + getProperties
        QueueStrategyResolve(ref id, objectId);
        // visible
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // stable
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // receives-events: bounding box
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 100, ""y"": 200, ""width"": 80, ""height"": 30}}}}}}}}");
        // DOM.getNodeForLocation
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""backendNodeId"": 42}}}}");
        // DOM.resolveNode
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""object"": {{""type"": ""object"", ""objectId"": ""resolved-1""}}}}}}");
        // identity check
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
    }

    /// <summary>
    /// Queues strategy resolve (2 calls) + callFunctionOn eval response.
    /// </summary>
    private void QueueResolveAndEval(int startId, string objectId, string valueJson)
    {
        var id = startId;
        QueueStrategyResolve(ref id, objectId);
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{""result"": {valueJson}}}}}");
    }

    [TestMethod]
    public async Task TextContentAsync_ResolvesThenEvaluates()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#title");

        QueueResolveAndEval(9, "elem-1", """{"type": "string", "value": "Hello"}""");

        var result = await locator.TextContentAsync();
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public async Task ClickAsync_RunsActionabilityChecksThenDispatchesMouse()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("button.submit");

        QueueClickActionabilityResponses(9);

        // getBoundingClientRect for the click action itself
        _socket.QueueResponse("""{"id": 18, "sessionId": "session-1", "result": {"result": {"type": "object", "value": {"x": 100, "y": 200, "width": 80, "height": 30}}}}""");

        // Mouse: mouseMoved, mousePressed, mouseReleased
        _socket.QueueResponse("""{"id": 19, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 20, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 21, "sessionId": "session-1", "result": {}}""");

        await locator.ClickAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("mouseMoved")));
        Assert.IsTrue(allSent.Any(s => s.Contains("mousePressed")));
        Assert.IsTrue(allSent.Any(s => s.Contains("mouseReleased")));
    }

    [TestMethod]
    public async Task CountAsync_ReturnsElementCount()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("li.item");

        // Strategy resolve: evaluate returns array, getProperties returns 5 elements
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""
            {
                "id": 10,
                "sessionId": "session-1",
                "result": {
                    "result": [
                        {"name": "0", "value": {"type": "object", "objectId": "e-0"}},
                        {"name": "1", "value": {"type": "object", "objectId": "e-1"}},
                        {"name": "2", "value": {"type": "object", "objectId": "e-2"}},
                        {"name": "3", "value": {"type": "object", "objectId": "e-3"}},
                        {"name": "4", "value": {"type": "object", "objectId": "e-4"}},
                        {"name": "length", "value": {"type": "number", "value": 5}}
                    ]
                }
            }
            """);

        var result = await locator.CountAsync();
        Assert.AreEqual(5, result);
    }

    [TestMethod]
    public async Task First_CreatesLocatorWithNthIndexZero()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.card");
        var first = locator.First;

        Assert.IsNotNull(first);
        Assert.AreNotSame(locator, first);
    }

    [TestMethod]
    public async Task Last_CreatesLocatorWithNthIndexMinusOne()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.card");
        var last = locator.Last;

        Assert.IsNotNull(last);
        Assert.AreNotSame(locator, last);
    }

    [TestMethod]
    public async Task Nth_CreatesLocatorWithSpecificIndex()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.card");
        var nth = locator.Nth(2);

        Assert.IsNotNull(nth);
        Assert.AreNotSame(locator, nth);
    }

    [TestMethod]
    public async Task Filter_CreatesFilteredLocator()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.card");
        var filtered = locator.Filter(new LocatorOptions { HasText = "special" });

        Assert.IsNotNull(filtered);
        Assert.AreNotSame(locator, filtered);
    }

    [TestMethod]
    public async Task ChildLocator_ComposesSelectors()
    {
        var page = await CreatePageAsync();
        var parent = page.Locator("div.container");
        var child = parent.Locator("span.label");

        Assert.IsNotNull(child);
        Assert.AreNotSame(parent, child);
    }

    [TestMethod]
    public async Task GetByTestId_CreatesCorrectSelector()
    {
        var page = await CreatePageAsync();
        var locator = page.GetByTestId("submit-btn");

        Assert.IsNotNull(locator);
    }

    [TestMethod]
    public async Task CssPrefix_DispatchesToCssStrategy()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("css=div.test");

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "elem-1"}}, {"name": "length", "value": {"type": "number", "value": 1}}]}}""");

        var handles = await locator.ElementHandlesAsync();
        Assert.AreEqual(1, handles.Count);

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("querySelectorAll") && s.Contains("div.test")));
    }

    [TestMethod]
    public async Task XPathPrefix_DispatchesToXPathStrategy()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("xpath=//button");

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": []}}""");

        var handles = await locator.ElementHandlesAsync();
        Assert.AreEqual(0, handles.Count);

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("document.evaluate")));
    }

    [TestMethod]
    public async Task TextPrefix_DispatchesToTextStrategy()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("text=Click me");

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": []}}""");

        var handles = await locator.ElementHandlesAsync();
        Assert.AreEqual(0, handles.Count);

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("walkShadow") && s.Contains("Click me")),
            "Default pierceShadow=true should use shadow-piercing walkShadow JS");
    }

    [TestMethod]
    public async Task DataTestIdPrefix_DispatchesToTestIdStrategy()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("data-testid=submit-btn");

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": []}}""");

        var handles = await locator.ElementHandlesAsync();
        Assert.AreEqual(0, handles.Count);

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("data-testid") && s.Contains("submit-btn")));
    }

    [TestMethod]
    public async Task UnprefixedSelector_RoutesToCssStrategy()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.container");

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": []}}""");

        var handles = await locator.ElementHandlesAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("querySelectorAll") && s.Contains("div.container")));
    }

    [TestMethod]
    public async Task PierceShadow_DefaultsToTrue()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("css=div.shadow");

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": []}}""");

        await locator.ElementHandlesAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("queryShadow")),
            "Default locator should use shadow-piercing JS");
    }

    [TestMethod]
    public async Task PierceShadow_False_BypassesShadowTraversal()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("css=div.shadow", new LocatorOptions { PierceShadow = false });

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": []}}""");

        await locator.ElementHandlesAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsFalse(allSent.Any(s => s.Contains("queryShadow")),
            "PierceShadow=false should NOT use shadow-piercing JS");
        Assert.IsTrue(allSent.Any(s => s.Contains("querySelectorAll")),
            "PierceShadow=false should use plain querySelectorAll");
    }

    [TestMethod]
    public async Task PierceShadow_InheritedByChildLocator()
    {
        var page = await CreatePageAsync();
        var parent = page.Locator("div.container", new LocatorOptions { PierceShadow = false });
        var child = parent.First;

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "elem-1"}}, {"name": "length", "value": {"type": "number", "value": 1}}]}}""");
        // callFunctionOn for textContent
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "test"}}}""");

        await child.TextContentAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsFalse(allSent.Any(s => s.Contains("queryShadow")),
            "Child locator should inherit parent's PierceShadow=false");
    }

    [TestMethod]
    public async Task ResolveThrows_WhenNoElementFound()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#nonexistent");

        // Strategy resolve: evaluate returns array objectId, getProperties returns empty
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-empty"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": []}}""");

        await Assert.ThrowsExceptionAsync<ElementNotFoundException>(
            () => locator.TextContentAsync());
    }
}
