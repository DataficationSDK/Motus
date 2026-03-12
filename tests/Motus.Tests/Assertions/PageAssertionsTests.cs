using Motus.Abstractions;
using Motus.Assertions;
using Motus.Tests.Transport;

namespace Motus.Tests.Assertions;

[TestClass]
public class PageAssertionsTests
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

    private async Task<IPage> CreatePageWithFrameAsync(string url = "about:blank")
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        // Inject frame navigated + execution context so evaluation works
        _socket.Enqueue($$"""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-main",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "{{url}}"
                    }
                }
            }
            """);

        _socket.Enqueue("""
            {
                "method": "Runtime.executionContextCreated",
                "sessionId": "session-1",
                "params": {
                    "context": {
                        "id": 1,
                        "origin": "://",
                        "name": "",
                        "auxData": { "frameId": "frame-main", "isDefault": true }
                    }
                }
            }
            """);

        await Task.Delay(100);
        return page;
    }

    // --- ToHaveTitleAsync ---

    [TestMethod]
    public async Task ToHaveTitleAsync_Passes_WhenTitleMatches()
    {
        var page = await CreatePageWithFrameAsync();

        // Queue response for Runtime.evaluate (document.title)
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "My Page"}}}""");

        await Expect.That(page).ToHaveTitleAsync("My Page", new() { Timeout = 500 });
    }

    [TestMethod]
    public async Task ToHaveTitleAsync_Throws_WhenNoMatch()
    {
        var page = await CreatePageWithFrameAsync();

        for (var i = 0; i < 10; i++)
        {
            _socket.QueueResponse($@"{{""id"": {9 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""Wrong Title""}}}}}}");
        }

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(page).ToHaveTitleAsync("Correct Title", new() { Timeout = 300 }));
        Assert.AreEqual("Wrong Title", ex.Actual);
    }

    [TestMethod]
    public async Task Not_ToHaveTitleAsync_Passes_WhenNoMatch()
    {
        var page = await CreatePageWithFrameAsync();

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "string", "value": "Other Title"}}}""");

        await Expect.That(page).Not.ToHaveTitleAsync("Expected Title", new() { Timeout = 500 });
    }

    [TestMethod]
    public async Task Not_ToHaveTitleAsync_Throws_WhenMatch()
    {
        var page = await CreatePageWithFrameAsync();

        for (var i = 0; i < 10; i++)
        {
            _socket.QueueResponse($@"{{""id"": {9 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""My Page""}}}}}}");
        }

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(page).Not.ToHaveTitleAsync("My Page", new() { Timeout = 300 }));
        Assert.IsTrue(ex.Message.Contains("NOT"));
    }

    // --- ToHaveUrlAsync ---

    [TestMethod]
    public async Task ToHaveUrlAsync_Passes_WhenUrlMatches()
    {
        var page = await CreatePageWithFrameAsync("https://example.com/page");

        await Expect.That(page).ToHaveUrlAsync("https://example.com/page", new() { Timeout = 200 });
    }

    [TestMethod]
    public async Task ToHaveUrlAsync_Throws_WhenNoMatch()
    {
        var page = await CreatePageWithFrameAsync("https://example.com/wrong");

        await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(page).ToHaveUrlAsync("https://example.com/correct", new() { Timeout = 200 }));
    }

    [TestMethod]
    public async Task Not_ToHaveUrlAsync_Passes_WhenNoMatch()
    {
        var page = await CreatePageWithFrameAsync("https://example.com/other");

        await Expect.That(page).Not.ToHaveUrlAsync("https://example.com/expected", new() { Timeout = 200 });
    }
}
