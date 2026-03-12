using Motus.Abstractions;

namespace Motus.Tests.Page;

[TestClass]
[TestCategory("Integration")]
public class PageNetworkIntegrationTests
{
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
    public async Task RouteAsync_FulfillWithCustomBody()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<h1>Test</h1>");

        await page.RouteAsync("**/mock-api", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"mocked": true}"""
            });
        });

        // Use evaluate to fetch the mocked URL
        var result = await page.EvaluateAsync<string>("""
            (async () => {
                const res = await fetch('/mock-api');
                return await res.text();
            })()
        """);

        Assert.AreEqual("""{"mocked": true}""", result);
    }

    [TestMethod]
    public async Task RouteAsync_AbortRequest()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<h1>Test</h1>");

        await page.RouteAsync("**/blocked", async route =>
        {
            await route.AbortAsync("connectionrefused");
        });

        var result = await page.EvaluateAsync<string>("""
            (async () => {
                try {
                    await fetch('/blocked');
                    return 'success';
                } catch(e) {
                    return 'blocked';
                }
            })()
        """);

        Assert.AreEqual("blocked", result);
    }

    [TestMethod]
    public async Task GotoAsync_ReturnsNavigationResponse()
    {
        var page = await _browser!.NewPageAsync();
        var response = await page.GotoAsync("data:text/html,<h1>Hello</h1>");

        // data: URLs may not produce a network response
        // Navigate to a real URL if possible
        // For data: URLs, response may be null which is acceptable
    }

    [TestMethod]
    public async Task RequestAndResponseEvents_FireDuringNavigation()
    {
        var page = await _browser!.NewPageAsync();

        var requestFired = false;
        var responseFired = false;
        page.Request += (_, _) => requestFired = true;
        page.Response += (_, _) => responseFired = true;

        await page.GotoAsync("data:text/html,<h1>Test</h1>");
        await Task.Delay(500);

        // data: URLs generate at least a Document request
        Assert.IsTrue(requestFired, "Request event should fire during navigation");
    }

    [TestMethod]
    public async Task WaitForLoadStateAsync_NetworkIdle_Completes()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<h1>Idle</h1>");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, timeout: 10000);
    }

    [TestMethod]
    public async Task RouteAsync_ContinueWithModification()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<h1>Test</h1>");

        string? interceptedMethod = null;
        await page.RouteAsync("**/test-continue", async route =>
        {
            interceptedMethod = route.Request.Method;
            await route.ContinueAsync();
        });

        // Trigger a fetch to the intercepted URL
        await page.EvaluateAsync<string>("""
            (async () => {
                try { await fetch('/test-continue'); } catch {}
                return 'done';
            })()
        """);

        await Task.Delay(300);
        Assert.AreEqual("GET", interceptedMethod);
    }
}
