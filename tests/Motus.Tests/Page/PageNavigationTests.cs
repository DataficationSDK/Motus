using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Page;

[TestClass]
public class PageNavigationTests
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

    private async Task<Motus.Abstractions.IPage> CreatePageWithFrameAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        // Inject initial frame
        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-main",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "about:blank"
                    }
                }
            }
            """);

        await Task.Delay(100);

        return page;
    }

    [TestMethod]
    public async Task GotoAsync_SendsNavigateCommand()
    {
        var page = await CreatePageWithFrameAsync();

        var gotoTask = page.GotoAsync("https://example.com");

        // Page.navigate response
        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": { "frameId": "frame-main" }
            }
            """);

        // Fire load event to complete navigation
        _socket.Enqueue("""
            {
                "method": "Page.loadEventFired",
                "sessionId": "session-1",
                "params": { "timestamp": 1234567.0 }
            }
            """);

        await gotoTask;

        // Verify the navigate command was sent
        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var msg = _socket.GetSentJson(i);
            if (msg.Contains("Page.navigate") && msg.Contains("https://example.com"))
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected Page.navigate command with URL");
    }

    [TestMethod]
    public async Task GotoAsync_ThrowsOnNavigationError()
    {
        var page = await CreatePageWithFrameAsync();

        var gotoTask = page.GotoAsync("https://invalid.example");

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": { "frameId": "frame-main", "errorText": "net::ERR_NAME_NOT_RESOLVED" }
            }
            """);

        await Assert.ThrowsExceptionAsync<MotusNavigationException>(() => gotoTask);
    }

    [TestMethod]
    public async Task GotoAsync_WithDomContentLoaded_CompletesOnDomEvent()
    {
        var page = await CreatePageWithFrameAsync();

        var gotoTask = page.GotoAsync("https://example.com",
            new NavigationOptions { WaitUntil = WaitUntil.DOMContentLoaded });

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": { "frameId": "frame-main" }
            }
            """);

        _socket.Enqueue("""
            {
                "method": "Page.domContentEventFired",
                "sessionId": "session-1",
                "params": { "timestamp": 1234567.0 }
            }
            """);

        await gotoTask;
    }

    [TestMethod]
    public async Task ReloadAsync_SendsReloadCommand()
    {
        var page = await CreatePageWithFrameAsync();

        var reloadTask = page.ReloadAsync();

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": {}
            }
            """);

        _socket.Enqueue("""
            {
                "method": "Page.loadEventFired",
                "sessionId": "session-1",
                "params": { "timestamp": 1234567.0 }
            }
            """);

        await reloadTask;

        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var msg = _socket.GetSentJson(i);
            if (msg.Contains("Page.reload"))
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected Page.reload command");
    }

    [TestMethod]
    public async Task WaitForLoadStateAsync_NetworkIdle_CompletesWhenNoActiveRequests()
    {
        var page = await CreatePageWithFrameAsync();

        // No active requests, so NetworkIdle should complete quickly
        var task = page.WaitForLoadStateAsync(LoadState.NetworkIdle, timeout: 5000);
        await task;
    }
}
