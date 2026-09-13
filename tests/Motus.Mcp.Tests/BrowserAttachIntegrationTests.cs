using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests;

/// <summary>
/// Drives the server against a browser it did not start, end to end: reaching what is already open
/// in it, switching to it mid-session, and leaving it running afterwards.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class BrowserAttachIntegrationTests
{
    private RunningBrowserFixture? _browser;

    [TestInitialize]
    public async Task Setup()
    {
        _browser = await RunningBrowserFixture.TryStartAsync();
        if (_browser is null)
            Assert.Inconclusive("No browser found; skipping integration test.");
    }

    [TestCleanup]
    public void Cleanup() => _browser?.Dispose();

    private static string TextOf(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    [TestMethod]
    public async Task ConfiguredWithAnEndpoint_TheSessionDrivesTheTabThatWasAlreadyOpen()
    {
        await using var bundle = new McpSessionBundle(
            new McpServerLaunchOptions { Endpoint = _browser!.HttpEndpoint });

        var page = await bundle.Pages.GetOrCreateActivePageAsync();
        await page.GotoAsync("data:text/html,<title>attached</title><h1>attached</h1>");

        Assert.AreEqual("attached", await page.TitleAsync());
        Assert.IsTrue(bundle.Sessions.IsAttached);

        // The tab reached here is the browser's own, not one opened in a context beside it. A
        // second context would be invisible to whoever is using the browser, which is the failure
        // this pins.
        var tabs = await bundle.Pages.ListTabsAsync();
        Assert.AreEqual(1, tabs.Count, "attaching should adopt the open tab rather than add one");
        Assert.AreSame(page, tabs[0].Page);
    }

    [TestMethod]
    public async Task DisposingTheSession_LeavesTheBrowserRunning()
    {
        var bundle = new McpSessionBundle(
            new McpServerLaunchOptions { Endpoint = _browser!.HttpEndpoint });
        await bundle.Pages.GetOrCreateActivePageAsync();

        await bundle.DisposeAsync();

        // Give a termination, if one were going to happen, time to actually happen.
        await Task.Delay(500);
        Assert.IsTrue(_browser.IsRunning,
            "a browser the server did not start must survive the server going away");
    }

    [TestMethod]
    public async Task BrowserStatus_SaysWhichBrowserIsBeingDriven()
    {
        await using var bundle = new McpSessionBundle(
            new McpServerLaunchOptions { Endpoint = _browser!.HttpEndpoint });
        await bundle.Pages.GetOrCreateActivePageAsync();

        var status = TextOf(await SessionTools.BrowserStatusAsync(bundle.Pages, CancellationToken.None));

        StringAssert.Contains(status, "Attached");
        StringAssert.Contains(status, _browser.HttpEndpoint);
    }

    [TestMethod]
    public async Task BrowserAttach_WithoutTheOption_IsRefused()
    {
        await using var bundle = new McpSessionBundle(new McpServerLaunchOptions { Headless = true });

        var result = await BrowserTools.BrowserAttachAsync(
            _browser!.HttpEndpoint, bundle.Pages, CancellationToken.None, bundle.Security);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "--allow-attach");
        Assert.IsFalse(bundle.Sessions.IsAttached);
        Assert.IsTrue(_browser.IsRunning);
    }

    [TestMethod]
    public async Task BrowserAttach_SwitchesAwayFromTheBrowserTheServerStarted()
    {
        // Pointing the session at a browser that is already running is something the server has to
        // have been started with, so the tool is reached the way an operator who allowed it would.
        await using var bundle = new McpSessionBundle(
            new McpServerLaunchOptions { Headless = true, AllowAttach = true });

        try
        {
            var own = await bundle.Pages.GetOrCreateActivePageAsync();
            await own.GotoAsync("data:text/html,<title>ours</title>");
            Assert.IsFalse(bundle.Sessions.IsAttached);
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration test.");
            return;
        }

        var result = await BrowserTools.BrowserAttachAsync(
            _browser!.HttpEndpoint, bundle.Pages, CancellationToken.None, bundle.Security);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.IsTrue(bundle.Sessions.IsAttached);

        var attachedPage = await bundle.Pages.GetOrCreateActivePageAsync();
        await attachedPage.GotoAsync("data:text/html,<title>theirs</title>");
        Assert.AreEqual("theirs", await attachedPage.TitleAsync());
        Assert.IsTrue(_browser.IsRunning);
    }

    [TestMethod]
    public async Task BrowserAttach_ToAnEndpointThatAnswersNothing_ReportsWhyRatherThanThrowing()
    {
        await using var bundle = new McpSessionBundle(
            new McpServerLaunchOptions { Endpoint = _browser!.HttpEndpoint });

        var result = await BrowserTools.BrowserAttachAsync(
            "http://127.0.0.1:1", bundle.Pages, CancellationToken.None, bundle.Security);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "remote debugging port");
    }
}
