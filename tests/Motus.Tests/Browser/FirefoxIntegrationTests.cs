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
}
