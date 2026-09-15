using Motus.Tests.Transport;

namespace Motus.Tests.Selectors;

[TestClass]
public class RoleSelectorStrategyTests
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
    public void ParseRoleSelector_RoleOnly()
    {
        var (role, name) = RoleSelectorStrategy.ParseRoleSelector("button");
        Assert.AreEqual("button", role);
        Assert.IsNull(name);
    }

    [TestMethod]
    public void ParseRoleSelector_RoleWithName()
    {
        var (role, name) = RoleSelectorStrategy.ParseRoleSelector("""button[name="Submit"]""");
        Assert.AreEqual("button", role);
        Assert.AreEqual("Submit", name);
    }

    [TestMethod]
    public void ParseRoleSelector_RoleWithUnquotedName()
    {
        var (role, name) = RoleSelectorStrategy.ParseRoleSelector("button[name=Submit]");
        Assert.AreEqual("button", role);
        Assert.AreEqual("Submit", name);
    }

    [TestMethod]
    public void ParseRoleSelector_UnknownAttribute_IgnoresName()
    {
        var (role, name) = RoleSelectorStrategy.ParseRoleSelector("button[label=foo]");
        Assert.AreEqual("button", role);
        Assert.IsNull(name);
    }

    [TestMethod]
    public async Task ResolveAsync_SendsAccessibilityQueryAXTree()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        // Accessibility.enable
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        // Runtime.evaluate for the document element the query is rooted at
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "doc-1"}}}""");
        // Accessibility.queryAXTree
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"nodes": []}}""");

        var strategy = new RoleSelectorStrategy();
        var handles = await strategy.ResolveAsync("""button[name="Submit"]""", ((Motus.Page)page).GetFrameForSelectors());

        Assert.AreEqual(0, handles.Count);

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("Accessibility.enable")),
            "Should call Accessibility.enable");
        Assert.IsTrue(allSent.Any(s => s.Contains("Accessibility.queryAXTree")),
            "Should call Accessibility.queryAXTree");
        Assert.IsTrue(allSent.Any(s => s.Contains("button") && s.Contains("Submit")),
            "Should include role and name in queryAXTree params");
    }

    /// <summary>
    /// A page-level locator maps to no explicit execution context, and the query has to carry a
    /// root node anyway: queryAXTree rejects a call that names none, so a page-level role selector
    /// used to fail on the protocol instead of searching the main document.
    /// </summary>
    [TestMethod]
    public async Task ResolveAsync_PageLevel_RootsTheQueryAtTheMainDocument()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        // Accessibility.enable
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {}}""");
        // Runtime.evaluate for the document element
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "main-doc"}}}""");
        // Accessibility.queryAXTree
        _socket.QueueResponse("""{"id": 12, "sessionId": "session-1", "result": {"nodes": []}}""");

        var strategy = new RoleSelectorStrategy();
        await strategy.ResolveAsync("button", ((Motus.Page)page).GetFrameForSelectors());

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        var documentEval = allSent.FirstOrDefault(s =>
            s.Contains("Runtime.evaluate") && s.Contains("document.documentElement"));
        Assert.IsNotNull(documentEval,
            "The page-level query should resolve the main frame's document element.");
        Assert.IsFalse(documentEval.Contains("contextId"),
            "The main frame is the session default, so no explicit context should be named.");

        var query = allSent.FirstOrDefault(s => s.Contains("Accessibility.queryAXTree"));
        Assert.IsNotNull(query, "Should call Accessibility.queryAXTree");
        Assert.IsTrue(query.Contains("\"objectId\":\"main-doc\""),
            "The query should be rooted at the document element rather than sent with no root.");
    }

    /// <summary>
    /// When the document cannot be reached, which is what a frame torn down mid-query looks like,
    /// the strategy reports no matches rather than sending a rootless query that fails the call.
    /// </summary>
    [TestMethod]
    public async Task ResolveAsync_DocumentUnreachable_ReturnsNoMatches()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        // Accessibility.enable
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {}}""");
        // Runtime.evaluate reports a throwing evaluation
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "object"}, "exceptionDetails": {"text": "gone"}}}""");

        var strategy = new RoleSelectorStrategy();
        var handles = await strategy.ResolveAsync("button", ((Motus.Page)page).GetFrameForSelectors());

        Assert.AreEqual(0, handles.Count);

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsFalse(allSent.Any(s => s.Contains("Accessibility.queryAXTree")),
            "No query should go out when there is no root node to scope it to.");
    }
}
