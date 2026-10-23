using System.Text.RegularExpressions;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Page;

/// <summary>
/// <see cref="IPage.WaitForURLAsync(string, NavigationOptions?)"/> and its overloads, on the page
/// and on a frame, driven by protocol events from a fake browser.
/// </summary>
[TestClass]
public class PageWaitForUrlTests
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
    public async Task Cleanup() => await _transport.DisposeAsync();

    private static NavigationOptions Within(int ms) => new() { Timeout = ms };

    private async Task<IPage> CreatePageAsync(string url)
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

        Navigate("frame-main", url);
        await WaitUntilAsync(() => page.Url == url);

        return page;
    }

    private void Navigate(string frameId, string url, string? parentId = null)
    {
        var parent = parentId is null ? string.Empty : $"\"parentId\": \"{parentId}\",";
        _socket.Enqueue($$"""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": { "id": "{{frameId}}", {{parent}} "loaderId": "l", "name": "", "url": "{{url}}" }
                }
            }
            """);
    }

    private void NavigateWithinDocument(string frameId, string url, string type = "historyApi") =>
        _socket.Enqueue($$"""
            {
                "method": "Page.navigatedWithinDocument",
                "sessionId": "session-1",
                "params": { "frameId": "{{frameId}}", "url": "{{url}}", "navigationType": "{{type}}" }
            }
            """);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("The fake browser's event was never applied.");
            await Task.Delay(10);
        }
    }

    // ── String ──

    [TestMethod]
    public async Task String_AlreadyMatching_ReturnsWithoutAnEvent()
    {
        var page = await CreatePageAsync("https://example.test/orders?page=1");

        var wait = page.WaitForURLAsync("https://example.test/orders?page=1", Within(5_000));

        Assert.IsTrue(wait.IsCompleted, "A URL that already matches should not wait for anything.");
        await wait;
    }

    [TestMethod]
    public async Task String_OnAPageThatOnlyContainsIt_TimesOutWithTheLastUrl()
    {
        var page = await CreatePageAsync("https://example.test/account");

        var ex = await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => page.WaitForURLAsync("https://example.test/", Within(300)));

        Assert.AreEqual("https://example.test/account", ex.LastEvaluatedValue);
        StringAssert.Contains(ex.Message, "https://example.test/account");
    }

    [TestMethod]
    public async Task String_QueryPrefix_IsNotSatisfiedByALongerValue()
    {
        var page = await CreatePageAsync("https://example.test/orders?page=10");

        await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => page.WaitForURLAsync("https://example.test/orders?page=1", Within(300)));
    }

    [TestMethod]
    public async Task String_ReturnsWhenTheExactUrlArrives()
    {
        var page = await CreatePageAsync("https://example.test/account");

        var wait = page.WaitForURLAsync("https://example.test/", Within(5_000));
        Navigate("frame-main", "https://example.test/");

        await wait;
    }

    [TestMethod]
    public async Task Glob_ReturnsWhenAMatchingUrlArrives()
    {
        var page = await CreatePageAsync("https://example.test/start");

        var wait = page.WaitForURLAsync("**/orders/*/edit", Within(5_000));
        Navigate("frame-main", "https://example.test/orders/42/edit");

        await wait;
    }

    // ── Regex ──

    [TestMethod]
    public async Task Regex_ReturnsWhenAMatchingUrlArrives()
    {
        var page = await CreatePageAsync("https://example.test/orders?page=1");

        var wait = page.WaitForURLAsync(new Regex(@"page=10$"), Within(5_000));
        Assert.IsFalse(wait.IsCompleted);
        Navigate("frame-main", "https://example.test/orders?page=10");

        await wait;
    }

    [TestMethod]
    public async Task Regex_NotMatching_TimesOut()
    {
        var page = await CreatePageAsync("https://example.test/orders?page=1");

        var ex = await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => page.WaitForURLAsync(new Regex(@"page=2"), Within(300)));
        StringAssert.Contains(ex.Message, "/page=2/");
    }

    // ── Predicate ──

    [TestMethod]
    public async Task Predicate_ReturnsWhenItAccepts()
    {
        var page = await CreatePageAsync("https://example.test/start");

        var wait = page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/done", Within(5_000));
        Navigate("frame-main", "https://example.test/done?x=1");

        await wait;
    }

    [TestMethod]
    public async Task Predicate_ThatThrows_EndsTheWaitWithItsException()
    {
        var page = await CreatePageAsync("https://example.test/start");

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => page.WaitForURLAsync(_ => throw new InvalidOperationException("boom"), Within(5_000)));
    }

    [TestMethod]
    public async Task NullPattern_IsRejected()
    {
        var page = await CreatePageAsync("https://example.test/start");

        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            () => page.WaitForURLAsync((string)null!));
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            () => page.WaitForURLAsync((Regex)null!));
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(
            () => page.WaitForURLAsync((Func<string, bool>)null!));
    }

    // ── What the wait sees ──

    /// <summary>
    /// A URL the page passes through on its way somewhere else is still a URL it navigated to,
    /// even when it is replaced before a poll would have caught it.
    /// </summary>
    [TestMethod]
    public async Task UrlPassedThroughBetweenPolls_IsStillSeen()
    {
        var page = await CreatePageAsync("https://example.test/start");

        var wait = page.WaitForURLAsync("https://example.test/login", Within(2_000));

        Navigate("frame-main", "https://example.test/login");
        Navigate("frame-main", "https://example.test/home");

        await wait;
    }

    /// <summary>
    /// The history API and fragment navigations change the URL without a new document, and the
    /// browser reports them with an event of their own rather than as a frame navigation.
    /// </summary>
    [TestMethod]
    public async Task NavigationWithinTheDocument_UpdatesTheUrlAndSatisfiesTheWait()
    {
        var page = await CreatePageAsync("https://example.test/app");
        IFrame? navigated = null;
        page.FrameNavigated += (_, f) => navigated = f;

        var wait = page.WaitForURLAsync("https://example.test/app/orders/42", Within(5_000));
        NavigateWithinDocument("frame-main", "https://example.test/app/orders/42");
        await wait;

        Assert.AreEqual("https://example.test/app/orders/42", page.Url);
        Assert.AreSame(page.MainFrame, navigated, "A same-document navigation should raise FrameNavigated.");

        NavigateWithinDocument("frame-main", "https://example.test/app/orders/42#notes", "fragment");
        await page.WaitForURLAsync("**#notes", Within(5_000));
    }

    [TestMethod]
    public async Task NavigationWithinTheDocument_ForAnUnknownFrame_IsIgnored()
    {
        var page = await CreatePageAsync("https://example.test/app");

        NavigateWithinDocument("no-such-frame", "https://example.test/elsewhere");
        Navigate("frame-main", "https://example.test/after");
        await WaitUntilAsync(() => page.Url == "https://example.test/after");
    }

    // ── Frames ──

    [TestMethod]
    public async Task Frame_WaitsOnItsOwnUrl_NotThePages()
    {
        var page = await CreatePageAsync("https://example.test/host");
        Navigate("frame-child", "https://example.test/child", parentId: "frame-main");
        await WaitUntilAsync(() => page.Frames.Count == 2);
        var child = page.Frames.Single(f => f != page.MainFrame);

        await child.WaitForURLAsync("https://example.test/child", Within(1_000));
        await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => child.WaitForURLAsync("https://example.test/host", Within(300)));

        var wait = child.WaitForURLAsync(new Regex(@"/child\?step=2$"), Within(5_000));

        // The page moving does not satisfy a wait on the frame.
        NavigateWithinDocument("frame-main", "https://example.test/host?step=2");
        await Task.Delay(200);
        Assert.IsFalse(wait.IsCompleted);

        Navigate("frame-child", "https://example.test/child?step=2", parentId: "frame-main");
        await wait;

        var predicateWait = child.WaitForURLAsync(url => url.EndsWith("#done", StringComparison.Ordinal), Within(5_000));
        NavigateWithinDocument("frame-child", "https://example.test/child?step=2#done", "fragment");
        await predicateWait;

        Assert.AreEqual("https://example.test/host?step=2", page.Url);
    }

    [TestMethod]
    public async Task Page_IsNotSatisfiedByAChildFrameReachingTheUrl()
    {
        var page = await CreatePageAsync("https://example.test/host");

        var wait = page.WaitForURLAsync("https://example.test/child", Within(400));
        Navigate("frame-child", "https://example.test/child", parentId: "frame-main");

        await Assert.ThrowsExceptionAsync<WaitTimeoutException>(() => wait);
    }
}
