using Motus.Abstractions;

namespace Motus.Tests.Browser;

[TestClass]
[TestCategory("Integration")]
[TestCategory("Firefox")]
public class FirefoxIntegrationTests
{
    private static bool FirefoxAvailable()
    {
        try
        {
            BrowserFinder.Resolve(BrowserChannel.Firefox, executablePath: null);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    [TestMethod]
    public async Task LaunchAsync_Firefox_ReturnsConnectedBrowser()
    {
        if (!FirefoxAvailable())
        {
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        await using var browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });

        Assert.IsTrue(browser.IsConnected);
    }

    [TestMethod]
    public async Task LaunchAsync_Firefox_Version_IsNotEmpty()
    {
        if (!FirefoxAvailable())
        {
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        await using var browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });

        Assert.IsFalse(string.IsNullOrEmpty(browser.Version));
    }

    [TestMethod]
    public async Task Firefox_CloseAsync_PerformsCleanShutdown()
    {
        if (!FirefoxAvailable())
        {
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        var browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });

        await browser.CloseAsync();

        Assert.IsFalse(browser.IsConnected);

        await browser.DisposeAsync();
    }

    [TestMethod]
    public async Task Firefox_NewPage_CanNavigate()
    {
        if (!FirefoxAvailable())
        {
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        await using var browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync("about:blank");

        Assert.IsNotNull(page);
    }

    [TestMethod]
    public async Task Firefox_NewPage_CanEvaluateScript()
    {
        if (!FirefoxAvailable())
        {
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        await using var browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var result = await page.EvaluateAsync<int>("1 + 1");

        Assert.AreEqual(2, result);
    }

    /// <summary>
    /// A new Firefox page has a main frame and takes content before anything has navigated it,
    /// the same as a new Chromium page.
    /// </summary>
    [TestMethod]
    public async Task Firefox_NewPage_HasMainFrameAndTakesContentWithoutNavigation()
    {
        if (!FirefoxAvailable())
        {
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        await using var browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });

        var context = await browser.NewContextAsync();
        var page = await context.NewPageAsync();

        Assert.AreEqual("about:blank", page.Url);
        Assert.AreEqual("about:blank", page.MainFrame.Url);
        Assert.AreEqual(1, page.Frames.Count);

        await page.SetContentAsync("<title>T</title><p id=x>hi</p>");

        Assert.AreEqual("T", await page.TitleAsync());
        Assert.AreEqual(2, await page.MainFrame.EvaluateAsync<int>("1 + 1"));
    }

    /// <summary>
    /// Evaluating straight after a navigation works on Firefox too. BiDi addresses script to a
    /// browsing context rather than to an execution context id, so there is no id to go stale, and
    /// the waiting Chromium needs must not get in its way.
    /// </summary>
    [TestMethod]
    public async Task Firefox_EvaluateRightAfterNavigation_Succeeds()
    {
        if (!FirefoxAvailable())
        {
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        await using var browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });

        for (var i = 0; i < 30; i++)
        {
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();

            await page.GotoAsync("about:blank");
            Assert.AreEqual(2, await page.EvaluateAsync<int>("1+1"), $"iteration {i}");

            await page.GotoAsync($"data:text/html,<title>t{i}</title>");
            Assert.AreEqual($"t{i}", await page.TitleAsync(), $"iteration {i}");

            await context.CloseAsync();
        }
    }
}
