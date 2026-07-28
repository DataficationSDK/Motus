using Motus.Abstractions;

namespace Motus.Tests.Locator;

/// <summary>
/// Pins that a locator built from a frame resolves inside that frame.
/// </summary>
/// <remarks>
/// The fixture uses <c>srcdoc</c> frames so both render in the page's own process, which is what
/// keeps the execution-context map populated without needing a server to serve real origins.
/// Assertions read state back through <see cref="IFrame.EvaluateAsync{T}"/>, which was already
/// frame-scoped before locators were, so it is an independent check rather than the same code
/// path confirming itself.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class FrameLocatorTests
{
    private IBrowser? _browser;
    private IPage? _page;

    // Two frames with identical markup and a per-frame marker, so a resolution that lands in the
    // wrong frame produces a wrong answer rather than an accidentally right one.
    private const string FixtureHtml = """
        data:text/html,
        <body>
          <button id="target" role="button" data-testid="go" aria-label="Go">main</button>
          <iframe id="one" srcdoc="
            <body>
              <button id='target' role='button' data-testid='go' aria-label='Go'>alpha</button>
              <div class='box'><span class='leaf'>alpha-leaf</span></div>
              <script>window.marker='alpha';window.clicks=0;
                document.getElementById('target').onclick=()=>window.clicks++;</script>
            </body>"></iframe>
          <iframe id="two" srcdoc="
            <body>
              <button id='target' role='button' data-testid='go' aria-label='Go'>beta</button>
              <div class='box'><span class='leaf'>beta-leaf</span></div>
              <script>window.marker='beta';window.clicks=0;
                document.getElementById('target').onclick=()=>window.clicks++;</script>
            </body>"></iframe>
        </body>
        """;

    [TestInitialize]
    public async Task Setup()
    {
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
        await _page.GotoAsync(FixtureHtml.Replace("\n", "").Replace("\r", ""));
        await WaitForFramesAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    // Frame attachment and its execution context arrive as events after the navigation settles.
    private async Task WaitForFramesAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (_page!.Frames.Count >= 3)
            {
                try
                {
                    foreach (var frame in ChildFrames())
                        await frame.EvaluateAsync<string>("window.marker");
                    return;
                }
                catch (InvalidOperationException)
                {
                    // Context not published yet.
                }
            }

            await Task.Delay(100);
        }

        Assert.Inconclusive("The fixture's frames never became addressable.");
    }

    private List<IFrame> ChildFrames() =>
        _page!.Frames.Where(f => f != _page.MainFrame).ToList();

    // Returns the two child frames keyed by their in-page marker, so a test never depends on
    // the order frames happen to be attached in.
    private async Task<(IFrame Alpha, IFrame Beta)> FramesAsync()
    {
        IFrame? alpha = null, beta = null;
        foreach (var frame in ChildFrames())
        {
            var marker = await frame.EvaluateAsync<string>("window.marker");
            if (marker == "alpha") alpha = frame;
            else if (marker == "beta") beta = frame;
        }

        Assert.IsNotNull(alpha, "The alpha frame was not found.");
        Assert.IsNotNull(beta, "The beta frame was not found.");
        return (alpha, beta);
    }

    [TestMethod]
    public async Task Locator_ResolvesInsideItsOwnFrame()
    {
        var (alpha, beta) = await FramesAsync();

        Assert.AreEqual("alpha", await alpha.Locator("#target").TextContentAsync());
        Assert.AreEqual("beta", await beta.Locator("#target").TextContentAsync());
    }

    [TestMethod]
    public async Task PageLocator_StillResolvesAgainstTheMainFrame()
    {
        Assert.AreEqual("main", await _page!.Locator("#target").TextContentAsync());
    }

    [TestMethod]
    public async Task Click_ActsInTheFrameThatWasAskedFor()
    {
        var (alpha, beta) = await FramesAsync();

        await alpha.Locator("#target").ClickAsync();

        Assert.AreEqual(1, await alpha.EvaluateAsync<int>("window.clicks"));
        Assert.AreEqual(0, await beta.EvaluateAsync<int>("window.clicks"),
            "A click on one frame's button reached the other frame.");
    }

    [TestMethod]
    public async Task GetByTestId_ScopesToTheFrame()
    {
        var (alpha, beta) = await FramesAsync();

        Assert.AreEqual("alpha", await alpha.GetByTestId("go").TextContentAsync());
        Assert.AreEqual("beta", await beta.GetByTestId("go").TextContentAsync());
    }

    [TestMethod]
    public async Task GetByText_ScopesToTheFrame()
    {
        var (alpha, beta) = await FramesAsync();

        Assert.AreEqual("alpha-leaf", await alpha.GetByText("alpha-leaf").Last.TextContentAsync());

        await Assert.ThrowsExceptionAsync<ElementNotFoundException>(
            () => beta.GetByText("alpha-leaf").Last.TextContentAsync(null),
            "Text from one frame was matched in the other.");
    }

    [TestMethod]
    public async Task GetByRole_ScopesToTheFrame()
    {
        var (alpha, beta) = await FramesAsync();

        Assert.AreEqual("alpha", await alpha.GetByRole("button", "Go").TextContentAsync());
        Assert.AreEqual("beta", await beta.GetByRole("button", "Go").TextContentAsync());
    }

    // GetByRole builds an attribute selector, so it never reaches the accessibility-tree strategy.
    // That strategy scopes by node rather than by execution context, so it is pinned separately.
    [TestMethod]
    public async Task RoleSelector_ScopesToTheFrame()
    {
        var (alpha, beta) = await FramesAsync();

        Assert.AreEqual("alpha", await alpha.Locator("""role=button[name="Go"]""").TextContentAsync());
        Assert.AreEqual("beta", await beta.Locator("""role=button[name="Go"]""").TextContentAsync());
    }

    [TestMethod]
    public async Task ChainingAndNth_PreserveTheFrameRoot()
    {
        var (alpha, beta) = await FramesAsync();

        Assert.AreEqual("alpha-leaf", await alpha.Locator(".box").Locator(".leaf").TextContentAsync());
        Assert.AreEqual("beta-leaf", await beta.Locator(".box").Locator(".leaf").TextContentAsync());

        Assert.AreEqual("alpha-leaf", await alpha.Locator(".leaf").First.TextContentAsync());
        Assert.AreEqual("alpha-leaf", await alpha.Locator(".leaf").Nth(0).TextContentAsync());
        Assert.AreEqual("alpha-leaf", await alpha.Locator(".leaf").Last.TextContentAsync());
    }

    [TestMethod]
    public async Task Filter_PreservesTheFrameRoot()
    {
        var (alpha, beta) = await FramesAsync();

        Assert.AreEqual("alpha-leaf",
            await alpha.Locator("span").Filter(new LocatorOptions { HasText = "alpha-leaf" }).TextContentAsync());

        await Assert.ThrowsExceptionAsync<ElementNotFoundException>(
            () => beta.Locator("span").Filter(new LocatorOptions { HasText = "alpha-leaf" }).TextContentAsync(null));
    }

    [TestMethod]
    public async Task ParentNavigation_PreservesTheFrameRoot()
    {
        var (alpha, _) = await FramesAsync();

        var boxClass = await alpha.Locator(".leaf").Locator("..").GetAttributeAsync("class");
        Assert.AreEqual("box", boxClass);
    }

    [TestMethod]
    public async Task GotoAsync_NavigatesTheFrameAndNotThePage()
    {
        var (alpha, beta) = await FramesAsync();
        var pageUrlBefore = _page!.Url;

        // A data: URL is refused inside a subframe by the browser, so the frame is navigated to
        // about:blank and given content afterwards.
        await alpha.GotoAsync("about:blank");
        await alpha.SetContentAsync("<p id='moved'>navigated</p>");

        Assert.AreEqual("about:blank", alpha.Url);
        Assert.AreEqual("navigated", await alpha.Locator("#moved").TextContentAsync());
        Assert.AreEqual(pageUrlBefore, _page.Url, "Navigating a frame moved the whole page.");
        Assert.AreEqual("beta", await beta.Locator("#target").TextContentAsync(),
            "Navigating one frame disturbed the other.");
    }

    [TestMethod]
    public async Task AddStyleTagAsync_AppliesToTheFrameAndNotThePage()
    {
        var (alpha, beta) = await FramesAsync();

        await alpha.AddStyleTagAsync(content: "#target { color: rgb(1, 2, 3); }");

        Assert.AreEqual(1, await alpha.EvaluateAsync<int>("document.querySelectorAll('style').length"));
        Assert.AreEqual(0, await beta.EvaluateAsync<int>("document.querySelectorAll('style').length"),
            "A style tag added to one frame landed in the other.");
        Assert.AreEqual(0, await _page!.EvaluateAsync<int>("document.querySelectorAll('style').length"),
            "A style tag added to a frame landed in the page.");
    }
}
