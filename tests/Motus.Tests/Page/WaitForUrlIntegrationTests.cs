using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Motus.Abstractions;
using Motus.Assertions;

namespace Motus.Tests.Page;

/// <summary>
/// Waits on a real browser for a page or a frame to reach a URL, by string, glob, regex and
/// predicate, across full navigations and same-document ones.
/// </summary>
/// <remarks>
/// The documents are served over HTTP from a loopback origin rather than as <c>data:</c> URLs,
/// because the history API refuses to run in an opaque origin and the point of several of these
/// tests is a URL that changes without the document changing.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class WaitForUrlIntegrationTests
{
    private UrlFixtureServer _server = null!;
    private IBrowser? _browser;
    private IPage? _page;

    [TestInitialize]
    public async Task Setup()
    {
        _server = new UrlFixtureServer();

        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
            return;
        }

        _page = await _browser.NewPageAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();

        _server?.Dispose();
    }

    private static NavigationOptions Within(int ms) => new() { Timeout = ms };

    // Navigates the page from script after a short delay, so the wait is already listening when
    // the URL changes.
    private Task NavigateLaterAsync(string script) =>
        _page!.EvaluateAsync<object?>($"setTimeout(() => {{ {script} }}, 150)");

    /// <summary>
    /// The case the change exists for: a site's root is a prefix of every other URL on the site,
    /// so a wait that only checked containment returned at once from any page.
    /// </summary>
    [TestMethod]
    public async Task Page_PlainString_DoesNotMatchAPageThatMerelyContainsIt()
    {
        await _page!.GotoAsync(_server.Origin + "/account");

        await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => _page.WaitForURLAsync(_server.Origin + "/", Within(500)));
    }

    [TestMethod]
    public async Task Page_PlainString_ReturnsOnceTheExactUrlLoads()
    {
        await _page!.GotoAsync(_server.Origin + "/account");

        await NavigateLaterAsync("location.href = '/';");
        await _page.WaitForURLAsync(_server.Origin + "/", Within(10_000));

        Assert.AreEqual(_server.Origin + "/", _page.Url);
    }

    /// <summary>
    /// A base URL is usually written without a trailing slash, while the browser reports the
    /// site's root with one.
    /// </summary>
    [TestMethod]
    public async Task Page_OriginWithoutATrailingSlash_MatchesTheSitesRoot()
    {
        await _page!.GotoAsync(_server.Origin + "/");

        await _page.WaitForURLAsync(_server.Origin, Within(1_000));
        await Expect.That(_page).ToHaveUrlAsync(_server.Origin, new AssertionOptions { Timeout = 1_000 });
    }

    [TestMethod]
    public async Task Page_UrlAlreadyMatching_ReturnsWithoutANavigation()
    {
        await _page!.GotoAsync(_server.Origin + "/orders?page=1");

        await _page.WaitForURLAsync(_server.Origin + "/orders?page=1", Within(1_000));
        await _page.WaitForURLAsync("**/orders?page=1", Within(1_000));
        await _page.WaitForURLAsync(new Regex(@"/orders\?page=1$"), Within(1_000));
        await _page.WaitForURLAsync(url => url.EndsWith("page=1", StringComparison.Ordinal), Within(1_000));
    }

    [TestMethod]
    public async Task Page_Glob_MatchesTheWholeUrl()
    {
        await _page!.GotoAsync(_server.Origin + "/orders/42/edit");

        // A glob that only covers a prefix of the URL is not a match.
        await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => _page.WaitForURLAsync("**/orders/42", Within(500)));

        await _page.WaitForURLAsync("**/orders/*/edit", Within(1_000));
        await _page.WaitForURLAsync("**/orders**", Within(1_000));
    }

    [TestMethod]
    public async Task Page_Regex_ReturnsOnceAMatchingUrlLoads()
    {
        await _page!.GotoAsync(_server.Origin + "/orders?page=1");

        await NavigateLaterAsync("location.href = '/orders?page=10';");
        await _page.WaitForURLAsync(new Regex(@"page=10$"), Within(10_000));

        StringAssert.EndsWith(_page.Url, "page=10");
    }

    [TestMethod]
    public async Task Page_Predicate_ReturnsOnceItAccepts()
    {
        await _page!.GotoAsync(_server.Origin + "/start");

        await NavigateLaterAsync("location.href = '/done';");
        await _page.WaitForURLAsync(url => new Uri(url).AbsolutePath == "/done", Within(10_000));

        Assert.AreEqual(_server.Origin + "/done", _page.Url);
    }

    /// <summary>
    /// A single-page application changes its URL through the history API without loading a new
    /// document, which is the change a URL wait is most often used to catch.
    /// </summary>
    [TestMethod]
    public async Task Page_HistoryPushState_IsSeenByTheWait()
    {
        await _page!.GotoAsync(_server.Origin + "/app");

        await NavigateLaterAsync("history.pushState({}, '', '/app/orders/42');");
        await _page.WaitForURLAsync("**/app/orders/42", Within(10_000));

        Assert.AreEqual(_server.Origin + "/app/orders/42", _page.Url);
    }

    [TestMethod]
    public async Task Page_FragmentNavigation_IsSeenByTheWait()
    {
        await _page!.GotoAsync(_server.Origin + "/app");

        await NavigateLaterAsync("location.hash = 'done';");
        await _page.WaitForURLAsync(_server.Origin + "/app#done", Within(10_000));

        Assert.AreEqual(_server.Origin + "/app#done", _page.Url);
    }

    /// <summary>
    /// A frame's wait is about the frame's own document. Checking the page's URL instead would
    /// return or time out according to the wrong document entirely.
    /// </summary>
    [TestMethod]
    public async Task Frame_WaitsOnItsOwnUrl_NotThePages()
    {
        var child = await HostFrameAsync();

        await child.WaitForURLAsync(_server.Origin + "/child", Within(2_000));

        await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => child.WaitForURLAsync(_server.Origin + "/host", Within(500)));
    }

    [TestMethod]
    public async Task Frame_Regex_ReturnsOnceTheFrameNavigates()
    {
        var child = await HostFrameAsync();

        await child.EvaluateAsync<object?>("setTimeout(() => { location.href = '/child?step=2'; }, 150)");
        await child.WaitForURLAsync(new Regex(@"/child\?step=2$"), Within(10_000));

        Assert.AreEqual(_server.Origin + "/host", _page!.Url, "The page itself should not have moved.");
    }

    [TestMethod]
    public async Task Frame_Predicate_SeesAHistoryPushStateInTheFrame()
    {
        var child = await HostFrameAsync();

        await child.EvaluateAsync<object?>("setTimeout(() => { history.pushState({}, '', '/child/next'); }, 150)");
        await child.WaitForURLAsync(url => url.EndsWith("/child/next", StringComparison.Ordinal), Within(10_000));

        Assert.AreEqual(_server.Origin + "/child/next", child.Url);
    }

    [TestMethod]
    public async Task Frame_Glob_DoesNotMatchAPrefixOfTheFrameUrl()
    {
        var child = await HostFrameAsync();

        await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => child.WaitForURLAsync(_server.Origin + "/chil", Within(500)));

        await child.WaitForURLAsync("**/child", Within(1_000));
    }

    private async Task<IFrame> HostFrameAsync()
    {
        await _page!.GotoAsync(_server.Origin + "/host");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var child = _page.Frames.FirstOrDefault(f => f != _page.MainFrame && f.Url.EndsWith("/child", StringComparison.Ordinal));
            if (child is not null)
                return child;

            await Task.Delay(50);
        }

        Assert.Inconclusive($"The child frame never appeared (saw {_page.Frames.Count} frames).");
        return null!;
    }
}

/// <summary>
/// The page-level waits again on Firefox, where URL changes arrive as WebDriver BiDi events and a
/// same-document navigation is reported as one of two different events.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[TestCategory("Firefox")]
public class WaitForUrlFirefoxIntegrationTests
{
    private UrlFixtureServer _server = null!;
    private IBrowser? _browser;
    private IPage? _page;

    [TestInitialize]
    public async Task Setup()
    {
        _server = new UrlFixtureServer();

        try
        {
            BrowserFinder.Resolve(BrowserChannel.Firefox, executablePath: null);
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        _browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });
        _page = await _browser.NewPageAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();

        _server?.Dispose();
    }

    private static NavigationOptions Within(int ms) => new() { Timeout = ms };

    private Task NavigateLaterAsync(string script) =>
        _page!.EvaluateAsync<object?>($"setTimeout(() => {{ {script} }}, 150)");

    [TestMethod]
    public async Task PlainString_MatchesOnlyTheExactUrl()
    {
        await _page!.GotoAsync(_server.Origin + "/account");

        await Assert.ThrowsExceptionAsync<WaitTimeoutException>(
            () => _page.WaitForURLAsync(_server.Origin + "/", Within(500)));

        await NavigateLaterAsync("location.href = '/';");
        await _page.WaitForURLAsync(_server.Origin + "/", Within(10_000));
    }

    [TestMethod]
    public async Task HistoryPushState_IsSeenByTheWait()
    {
        await _page!.GotoAsync(_server.Origin + "/app");

        await NavigateLaterAsync("history.pushState({}, '', '/app/orders/42');");
        await _page.WaitForURLAsync(new Regex(@"/app/orders/42$"), Within(10_000));

        Assert.AreEqual(_server.Origin + "/app/orders/42", _page.Url);
    }

    [TestMethod]
    public async Task FragmentNavigation_IsSeenByTheWait()
    {
        await _page!.GotoAsync(_server.Origin + "/app");

        await NavigateLaterAsync("location.hash = 'done';");
        await _page.WaitForURLAsync(url => url.EndsWith("#done", StringComparison.Ordinal), Within(10_000));

        Assert.AreEqual(_server.Origin + "/app#done", _page.Url);
    }
}

/// <summary>
/// Serves the same small document for every path, plus a host page that embeds a frame, from one
/// loopback origin.
/// </summary>
internal sealed class UrlFixtureServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();

    internal string Origin { get; }

    internal UrlFixtureServer()
    {
        Origin = $"http://127.0.0.1:{AllocateFreePort()}";
        _listener.Prefixes.Add(Origin + "/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    private async Task ServeAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            var body = path == "/host"
                ? "<!doctype html><html><body><p>host</p><iframe src=\"/child\"></iframe></body></html>"
                : $"<!doctype html><html><body><p>{WebUtility.HtmlEncode(path)}</p></body></html>";

            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";

            var bytes = Encoding.UTF8.GetBytes(body);
            context.Response.ContentLength64 = bytes.Length;

            try
            {
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception)
            {
                // The browser gave up on the request.
            }
        }
    }

    private static int AllocateFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
        _listener.Close();
    }
}
