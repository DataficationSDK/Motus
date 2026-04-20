using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Locator;

[TestClass]
public class LocatorDomScopingTests
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

    // Queues strategy resolve (Runtime.evaluate + Runtime.getProperties) for a base match of one element.
    private void QueueBaseResolveSingle(int startId, string arrObjectId, string elementObjectId)
    {
        _socket.QueueResponse($@"{{""id"": {startId}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""{arrObjectId}""}}}}}}");
        _socket.QueueResponse($@"{{""id"": {startId + 1}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""0"", ""value"": {{""type"": ""object"", ""objectId"": ""{elementObjectId}""}}}}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 1}}}}]}}}}");
    }

    // Queues strategy resolve with N element object ids.
    private void QueueBaseResolveMany(int startId, string arrObjectId, params string[] elementObjectIds)
    {
        _socket.QueueResponse($@"{{""id"": {startId}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""{arrObjectId}""}}}}}}");
        var items = string.Join(", ", elementObjectIds.Select((oid, i) =>
            $@"{{""name"": ""{i}"", ""value"": {{""type"": ""object"", ""objectId"": ""{oid}""}}}}"));
        _socket.QueueResponse($@"{{""id"": {startId + 1}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{items}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": {elementObjectIds.Length}}}}}]}}}}");
    }

    // Queues a descendant-query pair (Runtime.callFunctionOn + Runtime.getProperties) returning the given
    // descendant object ids under a parent. Empty childObjectIds means the descendant query matched nothing.
    private void QueueChildScopeResolve(int startId, string arrObjectId, params string[] childObjectIds)
    {
        _socket.QueueResponse($@"{{""id"": {startId}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""{arrObjectId}""}}}}}}");
        if (childObjectIds.Length == 0)
        {
            _socket.QueueResponse($@"{{""id"": {startId + 1}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 0}}}}]}}}}");
            return;
        }
        var items = string.Join(", ", childObjectIds.Select((oid, i) =>
            $@"{{""name"": ""{i}"", ""value"": {{""type"": ""object"", ""objectId"": ""{oid}""}}}}"));
        _socket.QueueResponse($@"{{""id"": {startId + 1}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{items}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": {childObjectIds.Length}}}}}]}}}}");
    }

    // --- Scoped chain under different parent strategies ---

    [TestMethod]
    public async Task ScopedChain_CssParent_QueriesDescendantsUnderParent()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.row").Locator(".cell");

        QueueBaseResolveSingle(9, "arr-rows", "row-1");
        QueueChildScopeResolve(11, "arr-cells", "cell-1");
        // Action: TextContent on cell-1
        _socket.QueueResponse("""{"id": 13, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "cell text"}}}""");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("cell text", text);

        // Verify the descendant query ran against the resolved parent (row-1) via callFunctionOn, not a
        // concatenated CSS string at the document root.
        var descendantCall = _socket.GetSentJson(10);
        StringAssert.Contains(descendantCall, "querySelectorAll", StringComparison.Ordinal);
        StringAssert.Contains(descendantCall, "\"objectId\":\"row-1\"", StringComparison.Ordinal);
        StringAssert.Contains(descendantCall, ".cell", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ScopedChain_XPathParent_QueriesDescendantsUnderResolvedElement()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("xpath=//section[@id='panel']").Locator(".cell");

        QueueBaseResolveSingle(9, "arr-xp", "panel-1");
        QueueChildScopeResolve(11, "arr-cells", "cell-1");
        _socket.QueueResponse("""{"id": 13, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "panel cell"}}}""");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("panel cell", text);

        // Parent resolve goes through the xpath strategy (document.evaluate); descendant query is CSS
        // under the resolved xpath element.
        var parentResolve = _socket.GetSentJson(8);
        StringAssert.Contains(parentResolve, "document.evaluate", StringComparison.Ordinal);

        var descendantCall = _socket.GetSentJson(10);
        StringAssert.Contains(descendantCall, "\"objectId\":\"panel-1\"", StringComparison.Ordinal);
        StringAssert.Contains(descendantCall, "querySelectorAll", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ScopedChain_TestIdParent_QueriesDescendantsUnderResolvedElement()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("data-testid=grid").Locator(".row");

        QueueBaseResolveSingle(9, "arr-testid", "grid-1");
        QueueChildScopeResolve(11, "arr-rows", "row-1");
        _socket.QueueResponse("""{"id": 13, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "row"}}}""");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("row", text);

        var parentResolve = _socket.GetSentJson(8);
        StringAssert.Contains(parentResolve, "data-testid", StringComparison.Ordinal);

        var descendantCall = _socket.GetSentJson(10);
        StringAssert.Contains(descendantCall, "\"objectId\":\"grid-1\"", StringComparison.Ordinal);
    }

    // --- Nth-on-base preserved through scoped chain ---

    [TestMethod]
    public async Task ScopedChain_FirstOnBase_ScopesToFirstParentOnly()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.row").First.Locator(".cell");

        // Base resolves to TWO rows — .First should pick row-A only, so descendant query runs once.
        QueueBaseResolveMany(9, "arr-rows", "row-A", "row-B");
        QueueChildScopeResolve(11, "arr-cells-A", "cell-A1");
        _socket.QueueResponse("""{"id": 13, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "A1"}}}""");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("A1", text);

        var descendantCall = _socket.GetSentJson(10);
        StringAssert.Contains(descendantCall, "\"objectId\":\"row-A\"", StringComparison.Ordinal);
        // Only one descendant call for the first row — no second parent resolution should appear.
        var allSent = Enumerable.Range(0, _socket.SentMessages.Count).Select(i => _socket.GetSentJson(i)).ToList();
        Assert.IsFalse(allSent.Any(s => s.Contains("\"objectId\":\"row-B\"")),
            "First() should restrict descendant query to the first base match only.");
    }

    // --- Multi-parent flatten and dedupe ---

    [TestMethod]
    public async Task ScopedChain_MultipleBaseMatches_FlattensDescendantsDeduped()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.row").Locator(".cell");

        // Base resolves to two rows.
        QueueBaseResolveMany(9, "arr-rows", "row-A", "row-B");

        // Parallel descendant queries: both callFunctionOn sends go out before either getProperties,
        // so the queue order must be [cFO_A, cFO_B, getProps_A, getProps_B] matching id sequence 11..14.
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-cells-A"}}}""");
        _socket.QueueResponse("""{"id": 12, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-cells-B"}}}""");
        _socket.QueueResponse("""{"id": 13, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "cell-1"}}, {"name": "1", "value": {"type": "object", "objectId": "cell-shared"}}, {"name": "length", "value": {"type": "number", "value": 2}}]}}""");
        _socket.QueueResponse("""{"id": 14, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "cell-shared"}}, {"name": "1", "value": {"type": "object", "objectId": "cell-2"}}, {"name": "length", "value": {"type": "number", "value": 2}}]}}""");

        var handles = await locator.ElementHandlesAsync();

        // Dedupe drops the duplicate "cell-shared" → 3 distinct handles.
        Assert.AreEqual(3, handles.Count);
        var ids = handles.Select(h => ((ElementHandle)h).ObjectId).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "cell-1", "cell-shared", "cell-2" }, ids.ToArray());
    }

    // --- Shadow piercing ---

    [TestMethod]
    public async Task ScopedChain_PierceShadowDefault_UsesQueryShadowInDescendantJs()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.host").Locator(".inner");

        QueueBaseResolveSingle(9, "arr-hosts", "host-1");
        QueueChildScopeResolve(11, "arr-inners", "inner-1");
        _socket.QueueResponse("""{"id": 13, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "x"}}}""");

        await locator.TextContentAsync();

        var descendantCall = _socket.GetSentJson(10);
        StringAssert.Contains(descendantCall, "queryShadow", StringComparison.Ordinal);
        StringAssert.Contains(descendantCall, ".inner", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task ScopedChain_PierceShadowFalse_UsesPlainQuerySelectorAll()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.host", new LocatorOptions { PierceShadow = false }).Locator(".inner");

        QueueBaseResolveSingle(9, "arr-hosts", "host-1");
        QueueChildScopeResolve(11, "arr-inners", "inner-1");
        _socket.QueueResponse("""{"id": 13, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "x"}}}""");

        await locator.TextContentAsync();

        var descendantCall = _socket.GetSentJson(10);
        Assert.IsFalse(descendantCall.Contains("queryShadow"),
            "PierceShadow=false should skip the shadow-walking variant of the descendant query.");
        StringAssert.Contains(descendantCall, "querySelectorAll", StringComparison.Ordinal);
    }

    // --- Zero-parent-match short circuit ---

    [TestMethod]
    public async Task ScopedChain_BaseMatchesZero_ThrowsWithNoDescendantCall()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.missing").Locator(".cell");

        // Base resolve returns an empty array — no descendant call should be issued.
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-empty"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": [{"name": "length", "value": {"type": "number", "value": 0}}]}}""");

        var ex = await Assert.ThrowsExceptionAsync<ElementNotFoundException>(() => locator.TextContentAsync());
        StringAssert.Contains(ex.Message, "No element matches parent selector", StringComparison.Ordinal);
        StringAssert.Contains(ex.Message, "No descendant query was attempted", StringComparison.Ordinal);

        // No callFunctionOn for descendants should have been issued.
        var allSent = Enumerable.Range(0, _socket.SentMessages.Count).Select(i => _socket.GetSentJson(i)).ToList();
        Assert.IsFalse(allSent.Any(s => s.Contains("querySelectorAll") && s.Contains(".cell")),
            "No descendant query should run when the base match set is empty.");
    }

    // --- Portal-aware diagnostic when parents match but descendants are empty ---

    [TestMethod]
    public async Task ScopedChain_ParentsMatchButDescendantsEmpty_ThrowsWithPortalHint()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.widget").Locator("#portaled");

        QueueBaseResolveSingle(9, "arr-widgets", "widget-1");
        // Descendant query returns empty NodeList under the only parent.
        QueueChildScopeResolve(11, "arr-empty");

        var ex = await Assert.ThrowsExceptionAsync<ElementNotFoundException>(() => locator.TextContentAsync());
        StringAssert.Contains(ex.Message, "matched 1 element(s)", StringComparison.Ordinal);
        StringAssert.Contains(ex.Message, "portal", StringComparison.Ordinal);
        StringAssert.Contains(ex.Message, "#portaled", StringComparison.Ordinal);
    }
}
