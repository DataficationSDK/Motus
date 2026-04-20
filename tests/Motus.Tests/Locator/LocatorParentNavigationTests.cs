using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Locator;

[TestClass]
public class LocatorParentNavigationTests
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

    // --- ParseParentPrefix unit tests ---

    [TestMethod]
    public void ParseParentPrefix_SingleDotDot_ReturnsOneStep()
    {
        var (steps, remaining) = Motus.Locator.ParseParentPrefix("..");
        Assert.AreEqual(1, steps);
        Assert.AreEqual(string.Empty, remaining);
    }

    [TestMethod]
    public void ParseParentPrefix_TwoDotDot_ReturnsTwoSteps()
    {
        var (steps, remaining) = Motus.Locator.ParseParentPrefix("../..");
        Assert.AreEqual(2, steps);
        Assert.AreEqual(string.Empty, remaining);
    }

    [TestMethod]
    public void ParseParentPrefix_ThreeLevels_ReturnsThreeSteps()
    {
        var (steps, remaining) = Motus.Locator.ParseParentPrefix("../../..");
        Assert.AreEqual(3, steps);
        Assert.AreEqual(string.Empty, remaining);
    }

    [TestMethod]
    public void ParseParentPrefix_ParentThenChild_ReturnsBoth()
    {
        var (steps, remaining) = Motus.Locator.ParseParentPrefix("../div.x");
        Assert.AreEqual(1, steps);
        Assert.AreEqual("div.x", remaining);
    }

    [TestMethod]
    public void ParseParentPrefix_PlainCss_ReturnsZeroSteps()
    {
        var (steps, remaining) = Motus.Locator.ParseParentPrefix("#id");
        Assert.AreEqual(0, steps);
        Assert.AreEqual("#id", remaining);
    }

    [TestMethod]
    public void ParseParentPrefix_CoincidentalDotsInCss_ReturnsZeroSteps()
    {
        // "..foo" is a CSS class-ish selector, not a parent step.
        var (steps, remaining) = Motus.Locator.ParseParentPrefix("..foo");
        Assert.AreEqual(0, steps);
        Assert.AreEqual("..foo", remaining);
    }

    [TestMethod]
    public void ParseParentPrefix_LeadingWhitespace_IsIgnored()
    {
        var (steps, remaining) = Motus.Locator.ParseParentPrefix("  ..  ");
        Assert.AreEqual(1, steps);
        Assert.AreEqual(string.Empty, remaining);
    }

    // --- Integration tests via FakeCdpSocket ---

    [TestMethod]
    public async Task LocatorDotDot_WalksUpOneParent()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#child").Locator("..");

        // Base resolve: Runtime.evaluate → array objectId, then getProperties → one child element
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-child"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "child-1"}}, {"name": "length", "value": {"type": "number", "value": 1}}]}}""");

        // Parent walk: Runtime.callFunctionOn on child-1 → parent-1
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "parent-1"}}}""");

        // TextContent on parent-1: Runtime.callFunctionOn → "parent text"
        _socket.QueueResponse("""{"id": 12, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "parent text"}}}""");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("parent text", text);

        // Sanity check: parent-walk call used our callFunctionOn with a parentElement loop
        var walkCall = _socket.GetSentJson(10);
        StringAssert.Contains(walkCall, "parentElement", StringComparison.Ordinal);
        StringAssert.Contains(walkCall, "\"objectId\":\"child-1\"", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task LocatorDotDotDotDot_WalksUpTwoParents()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#grandchild").Locator("../..");

        // Base resolve
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-gc"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "gc-1"}}, {"name": "length", "value": {"type": "number", "value": 1}}]}}""");

        // Single parent-walk call with steps=2 → grandparent
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "grandparent-1"}}}""");

        // TextContent on grandparent-1
        _socket.QueueResponse("""{"id": 12, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "grandparent"}}}""");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("grandparent", text);

        // The parent-walk call should carry steps=2 as the argument
        var walkCall = _socket.GetSentJson(10);
        StringAssert.Contains(walkCall, "\"value\":2", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task FirstThenDotDot_PreservesFirstIndexOnBase()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.cell").First.Locator("..");

        // Base resolve returns TWO cells
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-cells"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "cell-A"}}, {"name": "1", "value": {"type": "object", "objectId": "cell-B"}}, {"name": "length", "value": {"type": "number", "value": 2}}]}}""");

        // Parent-walk should only run on the FIRST cell (.First applied before walk)
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "row-A"}}}""");

        // TextContent on row-A
        _socket.QueueResponse("""{"id": 12, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "row A"}}}""");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("row A", text);

        var walkCall = _socket.GetSentJson(10);
        StringAssert.Contains(walkCall, "\"objectId\":\"cell-A\"", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task DotDotThenDescendant_QueriesUnderParent()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#child").Locator("..").Locator(".sibling");

        // Base resolve
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-child"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "child-1"}}, {"name": "length", "value": {"type": "number", "value": 1}}]}}""");

        // Parent walk
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "parent-1"}}}""");

        // Descendant query: Runtime.callFunctionOn on parent-1 → array objectId
        _socket.QueueResponse("""{"id": 12, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-siblings"}}}""");
        // Runtime.getProperties → sibling elements
        _socket.QueueResponse("""{"id": 13, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "sibling-1"}}, {"name": "length", "value": {"type": "number", "value": 1}}]}}""");

        // TextContent on sibling-1
        _socket.QueueResponse("""{"id": 14, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "sibling"}}}""");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("sibling", text);

        var descendantCall = _socket.GetSentJson(11);
        StringAssert.Contains(descendantCall, "querySelectorAll", StringComparison.Ordinal);
        StringAssert.Contains(descendantCall, "\"objectId\":\"parent-1\"", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task DotDotOnDetachedElement_ThrowsElementNotFound()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("html").Locator("..");

        // Base resolve returns html element
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-html"}}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"result": [{"name": "0", "value": {"type": "object", "objectId": "html-1"}}, {"name": "length", "value": {"type": "number", "value": 1}}]}}""");

        // Parent walk: parentElement of <html> is null → CDP returns result with no objectId
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "object", "subtype": "null", "value": null}}}""");

        await Assert.ThrowsExceptionAsync<ElementNotFoundException>(() => locator.TextContentAsync());
    }

    [TestMethod]
    public void DotDotAfterChildSelector_ThrowsClearError()
    {
        // Arrange a Locator with a child selector already applied.
        var page = new System.Threading.Tasks.Task<IPage>(() => throw new InvalidOperationException()); // not used here
        // We can build the chain without actually resolving:
        var fakeSetup = SetupNoNetwork();
        var scoped = fakeSetup.Locator("#x").Locator("..").Locator(".child");

        // Now chaining ".." onto an already-child-scoped locator should throw.
        var ex = Assert.ThrowsException<InvalidOperationException>(() => scoped.Locator(".."));
        StringAssert.Contains(ex.Message, "Cannot navigate with '..'", StringComparison.Ordinal);
    }

    // Build a Page without any network interaction — only used for chain construction tests
    // that don't actually resolve. The resolution would fail, but chain construction is sync.
    private IPage SetupNoNetwork()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        return _browser.NewPageAsync().GetAwaiter().GetResult();
    }
}
