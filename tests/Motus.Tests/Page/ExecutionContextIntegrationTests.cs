using Motus.Abstractions;

namespace Motus.Tests.Page;

/// <summary>
/// Evaluating the moment a navigation completes, against a real browser.
/// </summary>
/// <remarks>
/// A navigation's load event and the new document's execution context arrive separately, so a
/// caller that evaluates straight after <c>GotoAsync</c> could reach the page before the new
/// context had been recorded and be refused with "Cannot find context with specified id". It only
/// happened some of the time, so these tests repeat the sequence rather than relying on one run.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class ExecutionContextIntegrationTests
{
    private const int Iterations = 40;

    private CrossOriginFixtureServer _server = null!;
    private IBrowser? _browser;

    [TestInitialize]
    public async Task Setup()
    {
        _server = new CrossOriginFixtureServer();

        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = _server.IsolationArgs
            });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();

        _server?.Dispose();
    }

    /// <summary>
    /// The sequence the problem was reported with: a fresh context and page, one navigation, and
    /// an evaluation straight after it.
    /// </summary>
    [TestMethod]
    public async Task EvaluateRightAfterNavigatingAFreshPage_Succeeds()
    {
        for (var i = 0; i < Iterations; i++)
        {
            var context = await _browser!.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync("about:blank");
            Assert.AreEqual(2, await page.EvaluateAsync<int>("1+1"), $"iteration {i}");

            await page.GotoAsync($"data:text/html,<title>t{i}</title><p>{i}</p>");
            Assert.AreEqual($"t{i}", await page.TitleAsync(), $"iteration {i}");
            StringAssert.Contains(await page.ContentAsync(), $"<p>{i}</p>");

            await context.CloseAsync();
        }
    }

    /// <summary>
    /// A navigation to another site moves the page into a new renderer process, which replaces the
    /// main world with one numbered by a different process.
    /// </summary>
    [TestMethod]
    public async Task EvaluateRightAfterACrossOriginNavigation_RunsInTheNewDocument()
    {
        var page = await _browser!.NewPageAsync();

        for (var i = 0; i < Iterations / 2; i++)
        {
            await page.GotoAsync(_server.PrimaryOrigin + "/deep.html");
            Assert.AreEqual("deep", await page.EvaluateAsync<string>("window.marker"), $"iteration {i}");

            await page.GotoAsync(_server.SecondaryOrigin + "/middle.html");
            Assert.AreEqual("middle", await page.EvaluateAsync<string>("window.marker"), $"iteration {i}");
            Assert.AreEqual(_server.SecondaryOrigin, await page.EvaluateAsync<string>("location.origin"));
        }
    }

    /// <summary>
    /// A frame that navigates on its own replaces only its own main world. An evaluation in it must
    /// reach the new document, not the old one and not the page's main frame.
    /// </summary>
    [TestMethod]
    public async Task EvaluateInAFrameRightAfterItNavigates_RunsInThatFramesNewDocument()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(_server.OrderedUrl);
        var frame = await FirstChildFrameAsync(page);

        for (var i = 0; i < Iterations / 4; i++)
        {
            await frame.GotoAsync(_server.PrimaryOrigin + "/deep.html");
            Assert.AreEqual("deep", await frame.EvaluateAsync<string>("window.marker"), $"iteration {i}");
            Assert.AreEqual(0, await frame.EvaluateAsync<int>("window.clicks"));

            await frame.GotoAsync(_server.PrimaryOrigin + "/ordered-child.html?again=" + i);
            Assert.AreEqual(
                "child",
                await frame.EvaluateAsync<string>("document.querySelector('p').textContent"),
                $"iteration {i}");
        }

        Assert.IsNull(
            await page.EvaluateAsync<string?>("window.marker ?? null"),
            "the frame's evaluations never landed in the main frame");
    }

    /// <summary>
    /// Same as above for a locator, which resolves through the frame's context rather than through
    /// an evaluation of the caller's own.
    /// </summary>
    [TestMethod]
    public async Task LocatorInAFrameRightAfterItNavigates_ResolvesInThatFramesNewDocument()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(_server.OrderedUrl);
        var frame = await FirstChildFrameAsync(page);

        for (var i = 0; i < Iterations / 4; i++)
        {
            await frame.GotoAsync(_server.PrimaryOrigin + "/deep.html");
            Assert.AreEqual("deep", await frame.Locator("#target").TextContentAsync(), $"iteration {i}");

            await frame.GotoAsync(_server.PrimaryOrigin + "/ordered-child.html?again=" + i);
            Assert.AreEqual("child", await frame.Locator("p").TextContentAsync(), $"iteration {i}");
        }
    }

    private static async Task<IFrame> FirstChildFrameAsync(IPage page)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var frame = page.MainFrame.ChildFrames.FirstOrDefault(
                f => f.Url.EndsWith("/ordered-child.html?i=0", StringComparison.Ordinal));
            if (frame is not null)
                return frame;

            await Task.Delay(100);
        }

        Assert.Inconclusive("The fixture's first child frame never appeared.");
        return null!;
    }
}
