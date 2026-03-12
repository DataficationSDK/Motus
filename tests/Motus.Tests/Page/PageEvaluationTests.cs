using Motus.Tests.Transport;

namespace Motus.Tests.Page;

[TestClass]
public class PageEvaluationTests
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
        var page = await _browser.NewPageAsync();

        // Inject frame navigated + execution context
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

    [TestMethod]
    public async Task EvaluateAsync_ReturnsStringResult()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<string>("document.title");

        _socket.Enqueue("""
            {
                "id": 8,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "string", "value": "Test Page" }
                }
            }
            """);

        var result = await evalTask;
        Assert.AreEqual("Test Page", result);
    }

    [TestMethod]
    public async Task EvaluateAsync_ReturnsNumberResult()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<int>("1 + 1");

        _socket.Enqueue("""
            {
                "id": 8,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "number", "value": 2 }
                }
            }
            """);

        var result = await evalTask;
        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public async Task EvaluateAsync_ThrowsOnJsException()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<string>("throw new Error('boom')");

        _socket.Enqueue("""
            {
                "id": 8,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "undefined" },
                    "exceptionDetails": {
                        "exceptionId": 1,
                        "text": "Error: boom",
                        "lineNumber": 0,
                        "columnNumber": 0
                    }
                }
            }
            """);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => evalTask);
    }

    [TestMethod]
    public async Task EvaluateHandleAsync_ReturnsJsHandle()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateHandleAsync("document.body");

        _socket.Enqueue("""
            {
                "id": 8,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "object", "objectId": "obj-123" }
                }
            }
            """);

        var handle = await evalTask;
        Assert.IsNotNull(handle);
    }
}
