using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Page;

[TestClass]
public class PageEventsTests
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

    private async Task<Motus.Abstractions.IPage> CreatePageAsync()
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
    public async Task ConsoleEvent_FiresOnConsoleApiCalled()
    {
        var page = await CreatePageAsync();

        ConsoleMessageEventArgs? received = null;
        page.Console += (_, args) => received = args;

        _socket.Enqueue("""
            {
                "method": "Runtime.consoleAPICalled",
                "sessionId": "session-1",
                "params": {
                    "type": "log",
                    "args": [{ "type": "string", "value": "hello world" }],
                    "executionContextId": 1,
                    "timestamp": 1234567.0
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.AreEqual("log", received.Type);
        Assert.AreEqual("hello world", received.Text);
    }

    [TestMethod]
    public async Task DialogEvent_FiresOnJavascriptDialogOpening()
    {
        var page = await CreatePageAsync();

        DialogEventArgs? received = null;
        page.Dialog += (_, args) => received = args;

        _socket.Enqueue("""
            {
                "method": "Page.javascriptDialogOpening",
                "sessionId": "session-1",
                "params": {
                    "url": "about:blank",
                    "message": "Are you sure?",
                    "type": "confirm",
                    "hasBrowserHandler": false
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.AreEqual(DialogType.Confirm, received.Dialog.Type);
        Assert.AreEqual("Are you sure?", received.Dialog.Message);
    }

    [TestMethod]
    public async Task PageError_FiresOnExceptionThrown()
    {
        var page = await CreatePageAsync();

        PageErrorEventArgs? received = null;
        page.PageError += (_, args) => received = args;

        _socket.Enqueue("""
            {
                "method": "Runtime.exceptionThrown",
                "sessionId": "session-1",
                "params": {
                    "timestamp": 1234567.0,
                    "exceptionDetails": {
                        "exceptionId": 1,
                        "text": "Uncaught ReferenceError: foo is not defined",
                        "lineNumber": 1,
                        "columnNumber": 0
                    }
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.IsTrue(received.Message.Contains("foo is not defined"));
    }

    [TestMethod]
    public async Task FrameNavigated_UpdatesFrameUrl()
    {
        var page = await CreatePageAsync();

        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-1",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "https://example.com"
                    }
                }
            }
            """);

        await Task.Delay(100);

        Assert.AreEqual("https://example.com", page.Url);
        Assert.AreEqual(1, page.Frames.Count);
    }

    [TestMethod]
    public async Task FrameAttached_AddsChildFrame()
    {
        var page = await CreatePageAsync();

        // Inject main frame
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

        // Attach a child frame
        _socket.Enqueue("""
            {
                "method": "Page.frameAttached",
                "sessionId": "session-1",
                "params": {
                    "frameId": "frame-child",
                    "parentFrameId": "frame-main"
                }
            }
            """);

        await Task.Delay(100);

        Assert.AreEqual(2, page.Frames.Count);
        var mainFrame = page.MainFrame;
        Assert.AreEqual(1, mainFrame.ChildFrames.Count);
    }

    [TestMethod]
    public async Task FrameDetached_RemovesFrame()
    {
        var page = await CreatePageAsync();

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
                "method": "Page.frameAttached",
                "sessionId": "session-1",
                "params": {
                    "frameId": "frame-child",
                    "parentFrameId": "frame-main"
                }
            }
            """);

        await Task.Delay(100);
        Assert.AreEqual(2, page.Frames.Count);

        _socket.Enqueue("""
            {
                "method": "Page.frameDetached",
                "sessionId": "session-1",
                "params": {
                    "frameId": "frame-child"
                }
            }
            """);

        await Task.Delay(100);
        Assert.AreEqual(1, page.Frames.Count);
    }

    [TestMethod]
    public async Task DownloadEvent_FiresOnDownloadWillBegin()
    {
        var page = await CreatePageAsync();

        IDownload? received = null;
        page.Download += (_, d) => received = d;

        _socket.Enqueue("""
            {
                "method": "Page.downloadWillBegin",
                "sessionId": "session-1",
                "params": {
                    "frameId": "frame-1",
                    "guid": "dl-1",
                    "url": "https://example.com/file.zip",
                    "suggestedFilename": "file.zip"
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.AreEqual("https://example.com/file.zip", received.Url);
        Assert.AreEqual("file.zip", received.SuggestedFilename);
    }
}
