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
        return await _browser.NewPageAsync();
    }

    [TestMethod]
    public async Task TextContentAsync_ResolvesThenEvaluates()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#title");

        // resolve: Runtime.evaluate for querySelector
        _socket.QueueResponse("""
            {
                "id": 8,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "object", "objectId": "elem-1" }
                }
            }
            """);

        // callFunctionOn for textContent
        _socket.QueueResponse("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "string", "value": "Hello" }
                }
            }
            """);

        var result = await locator.TextContentAsync();
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public async Task ClickAsync_DispatchesMouseEvents()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("button.submit");

        // resolve: querySelector
        _socket.QueueResponse("""
            {
                "id": 8,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "object", "objectId": "btn-1" }
                }
            }
            """);

        // getBoundingClientRect via callFunctionOn
        _socket.QueueResponse("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "object", "value": { "x": 100, "y": 200, "width": 80, "height": 30 } }
                }
            }
            """);

        // Mouse: mouseMoved, mousePressed, mouseReleased
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 12, "sessionId": "session-1", "result": {}}""");

        await locator.ClickAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("mouseMoved")));
        Assert.IsTrue(allSent.Any(s => s.Contains("mousePressed")));
        Assert.IsTrue(allSent.Any(s => s.Contains("mouseReleased")));
    }

    [TestMethod]
    public async Task CountAsync_EvaluatesLength()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("li.item");

        _socket.QueueResponse("""
            {
                "id": 8,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "number", "value": 5 }
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
    public async Task ResolveThrows_WhenNoElementFound()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#nonexistent");

        // Return null object
        _socket.QueueResponse("""
            {
                "id": 8,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "object", "subtype": "null" }
                }
            }
            """);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => locator.TextContentAsync());
    }
}
