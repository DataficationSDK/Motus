using Motus.Abstractions;

namespace Motus.Tests.Context;

/// <summary>
/// Covers which pages a rule registered on the context reaches. The page it is easy to miss is
/// the one that was already open when the rule was registered, because nothing about that page
/// changes at registration time except whether interception is on.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class BrowserContextRouteIntegrationTests
{
    /// <summary>
    /// A port nothing is listening on, so a request that is not intercepted cannot succeed and a
    /// pass cannot come from somewhere else answering.
    /// </summary>
    private const string MockUrl = "http://127.0.0.1:19987/mocked";

    private const string MockPattern = "**/mocked";

    private const string Marker = "served-by-the-mock";

    private IBrowser? _browser;

    [TestInitialize]
    public async Task Setup()
    {
        try
        {
            BrowserFinder.Resolve(channel: null, executablePath: null);
        }
        catch
        {
            Assert.Inconclusive("No browser found for integration tests.");
            return;
        }

        _browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.CloseAsync();
    }

    [TestMethod]
    public async Task RouteAsync_RegisteredAfterPageOpen_MocksNavigationOnThatPage()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<h1>before</h1>");

        await page.Context.RouteAsync(MockPattern, FulfillWithMarkerAsync);

        await page.GotoAsync(MockUrl);

        StringAssert.Contains(await page.ContentAsync(), Marker);
    }

    [TestMethod]
    public async Task RouteAsync_RegisteredBeforePageOpen_MocksNavigationOnTheNewPage()
    {
        var context = await _browser!.NewContextAsync();
        await context.RouteAsync(MockPattern, FulfillWithMarkerAsync);

        var page = await context.NewPageAsync();
        await page.GotoAsync(MockUrl);

        StringAssert.Contains(await page.ContentAsync(), Marker);
    }

    [TestMethod]
    public async Task UnrouteAsync_AfterRegisteringOnOpenPage_LetsRequestsThroughAgain()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<h1>before</h1>");

        await page.Context.RouteAsync(MockPattern, FulfillWithMarkerAsync);
        StringAssert.Contains(await FetchMockUrlAsync(page), Marker, "The rule should be in force here");

        await page.Context.UnrouteAsync(MockPattern);

        Assert.AreEqual("request-failed", await FetchMockUrlAsync(page));
    }

    private static Task FulfillWithMarkerAsync(IRoute route) =>
        route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 200,
            ContentType = "text/html",
            Body = $"<h1 id=\"marker\">{Marker}</h1>",
            Headers = new Dictionary<string, string>
            {
                // The page asking is a data: URL, which is its own opaque origin.
                ["Access-Control-Allow-Origin"] = "*",
            },
        });

    /// <summary>
    /// Fetches the mocked URL from the page and returns the body, or "request-failed" when the
    /// request did not come back at all.
    /// </summary>
    private static Task<string> FetchMockUrlAsync(IPage page) =>
        page.EvaluateAsync<string>($$"""
            (async () => {
                try {
                    const res = await fetch('{{MockUrl}}');
                    return await res.text();
                } catch {
                    return 'request-failed';
                }
            })()
            """);
}
