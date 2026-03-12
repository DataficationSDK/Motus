using Motus.Tests.Transport;

namespace Motus.Tests.Page;

[TestClass]
public class PageTests
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
    public async Task Page_IsClosed_FalseInitially()
    {
        var page = await CreatePageAsync();
        Assert.IsFalse(page.IsClosed);
    }

    [TestMethod]
    public async Task Page_Context_ReturnsOwningContext()
    {
        var page = await CreatePageAsync();
        Assert.IsNotNull(page.Context);
        Assert.AreEqual(_browser, page.Context.Browser);
    }

    [TestMethod]
    public async Task Page_Video_ReturnsNull()
    {
        var page = await CreatePageAsync();
        Assert.IsNull(page.Video);
    }

    [TestMethod]
    public async Task Page_FrameNavigated_SetsMainFrame()
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

        await Task.Delay(100);

        Assert.AreEqual("about:blank", page.Url);
        Assert.IsNotNull(page.MainFrame);
        Assert.AreEqual("about:blank", page.MainFrame.Url);
    }

    [TestMethod]
    public async Task Page_Dispose_SetsIsClosed()
    {
        var page = await CreatePageAsync();
        await page.DisposeAsync();
        Assert.IsTrue(page.IsClosed);
    }

    [TestMethod]
    public async Task Page_Close_FiresCloseEvent()
    {
        var page = await CreatePageAsync();
        var closeFired = false;
        page.Close += (_, _) => closeFired = true;

        await page.DisposeAsync();

        Assert.IsTrue(closeFired);
    }
}
