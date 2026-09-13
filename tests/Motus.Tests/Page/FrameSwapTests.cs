using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Page;

/// <summary>
/// What the page does with <c>Page.frameDetached</c> when the browser has only moved a frame into
/// another renderer process rather than taken it away, and with the frames an old document hosted
/// once its frame commits a new one.
/// </summary>
[TestClass]
public class FrameSwapTests
{
    private MethodAwareFakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new MethodAwareFakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
        _browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        _socket.Respond(
            "Browser.getVersion",
            """{"result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");

        await _browser.InitializeAsync(CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task FrameDetached_WithSwapReason_KeepsTheFrameAndItsSession()
    {
        var page = await CreatePageWithOutOfProcessFrameAsync();

        _socket.Enqueue("""
            {
                "method": "Page.frameDetached",
                "sessionId": "session-1",
                "params": {
                    "frameId": "frame-oop",
                    "reason": "swap"
                }
            }
            """);

        await Task.Delay(150);

        var frame = page.Frames.FirstOrDefault(f => f.Url.EndsWith("child.html", StringComparison.Ordinal));
        Assert.IsNotNull(frame, "the swapped frame is still one of the page's frames");
        Assert.IsFalse(frame.IsDetached, "a frame that changed process has not been detached");
        Assert.IsTrue(
            ((global::Motus.Page)page).HasOwnSession(frame),
            "the frame is still reached over the session its own target opened");
    }

    [TestMethod]
    public async Task FrameDetached_WithoutAReason_RemovesTheFrame()
    {
        var page = await CreatePageWithOutOfProcessFrameAsync();

        _socket.Enqueue("""
            {
                "method": "Page.frameDetached",
                "sessionId": "session-1",
                "params": {
                    "frameId": "frame-oop"
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsFalse(
            page.Frames.Any(f => f.Url.EndsWith("child.html", StringComparison.Ordinal)),
            "a frame the browser really detached is gone from the page");
    }

    [TestMethod]
    public async Task FrameDetached_WithSwapReason_BeforeTheNewTargetClaimsTheFrame_KeepsItAndDropsWhatItHosted()
    {
        var page = await CreatePageWithOutOfProcessFrameAsync();
        AttachOnPageSession("frame-moving", parent: "frame-main");
        AttachOnPageSession("frame-inside", parent: "frame-moving");
        await Task.Delay(150);
        Assert.AreEqual(4, page.Frames.Count, "the page holds both frames the page session reported");

        // The swap notice for a frame still reached over the page session is the first word of the
        // move, so the frame is kept for the target about to claim it.
        _socket.Enqueue("""
            {
                "method": "Page.frameDetached",
                "sessionId": "session-1",
                "params": {
                    "frameId": "frame-moving",
                    "reason": "swap"
                }
            }
            """);

        await Task.Delay(150);

        var moving = page.Frames.FirstOrDefault(f => ((global::Motus.Frame)f).Id == "frame-moving");
        Assert.IsNotNull(moving, "the frame changing process is still one of the page's frames");
        Assert.IsFalse(moving.IsDetached, "a frame on its way to another process has not been detached");
        Assert.IsFalse(
            page.Frames.Any(f => ((global::Motus.Frame)f).Id == "frame-inside"),
            "what the moving frame hosted is dropped, since its new document reports its own frames");
    }

    [TestMethod]
    public async Task FrameNavigated_ToANewDocument_DropsTheFramesTheOldOneHosted()
    {
        var page = await CreatePageWithOutOfProcessFrameAsync();
        AttachOnPageSession("frame-same", parent: "frame-main");
        await Task.Delay(150);
        Assert.AreEqual(3, page.Frames.Count);

        // The old document goes into the back-forward cache, so the browser announces its frames
        // as swapped out rather than removed, and may do so after the navigation or not at all.
        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-main",
                        "loaderId": "loader-3",
                        "name": "",
                        "url": "http://a.test/next.html"
                    }
                }
            }
            """);
        _socket.Enqueue("""
            {
                "method": "Page.frameDetached",
                "sessionId": "session-1",
                "params": {
                    "frameId": "frame-oop",
                    "reason": "swap"
                }
            }
            """);

        await Task.Delay(150);

        Assert.AreEqual(1, page.Frames.Count, "only the main frame survives its own navigation");
        Assert.AreEqual("http://a.test/next.html", page.MainFrame.Url);
    }

    /// <summary>Reports a same-process frame the way the page session announces one.</summary>
    private void AttachOnPageSession(string frameId, string parent)
        => _socket.Enqueue($$"""
            {
                "method": "Page.frameAttached",
                "sessionId": "session-1",
                "params": {
                    "frameId": "{{frameId}}",
                    "parentFrameId": "{{parent}}"
                }
            }
            """);

    /// <summary>
    /// Builds a page with one frame the browser hosts in a process of its own, which is the only
    /// shape in which a swap is reported.
    /// </summary>
    private async Task<IPage> CreatePageWithOutOfProcessFrameAsync()
    {
        _socket.Respond("Target.createBrowserContext", """{"result": {"browserContextId": "ctx-1"}}""");
        _socket.Respond("Target.createTarget", """{"result": {"targetId": "target-1"}}""");
        _socket.Respond("Target.attachToTarget", """{"result": {"sessionId": "session-1"}}""");

        var page = await _browser.NewPageAsync();

        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-main",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "http://a.test/outer.html"
                    }
                }
            }
            """);

        await Task.Delay(100);

        // The frame's own target answers with a tree rooted at the frame, which is what records it
        // against the new session.
        _socket.Respond("Page.getFrameTree", """
            {
                "sessionId": "session-2",
                "result": {
                    "frameTree": {
                        "frame": {
                            "id": "frame-oop",
                            "parentId": "frame-main",
                            "loaderId": "loader-2",
                            "name": "",
                            "url": "http://b.test/child.html"
                        }
                    }
                }
            }
            """);

        _socket.Enqueue("""
            {
                "method": "Target.attachedToTarget",
                "sessionId": "session-1",
                "params": {
                    "sessionId": "session-2",
                    "waitingForDebugger": false,
                    "targetInfo": {
                        "targetId": "frame-oop",
                        "type": "iframe",
                        "title": "child",
                        "url": "http://b.test/child.html",
                        "attached": true
                    }
                }
            }
            """);

        await Task.Delay(250);

        Assert.AreEqual(2, page.Frames.Count, "the page starts with its main frame and the adopted one");
        return page;
    }
}
