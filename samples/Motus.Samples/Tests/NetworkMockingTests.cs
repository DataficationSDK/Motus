namespace Motus.Samples.Tests;

/// <summary>
/// Phase 2C: Network interception with RouteAsync, FulfillAsync, AbortAsync, and WaitForResponseAsync.
/// Routes are set up before loading page content so the Fetch domain intercepts from the start.
/// Note: FulfillAsync tests require a route-capable origin; they are tagged Integration
/// because about:blank and data: URIs have opaque origins that limit Fetch domain interception.
/// </summary>
[TestClass]
public class NetworkMockingTests : MotusTestBase
{
    [TestMethod]
    [Ignore("Requires a route-capable origin; FulfillAsync cannot intercept on about:blank/data: URIs")]
    public async Task RouteAsync_FulfillsWithJson()
    {
        // Intercept /api/data and return mock JSON
        await Page.RouteAsync("**/api/data", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"items": ["Alpha", "Bravo", "Charlie"]}"""
            });
        });

        await Fixtures.SetPageContentViaBlankAsync(Page, Fixtures.ApiPage);
        await Page.Locator("#fetch-btn").ClickAsync();

        await Expect.That(Page.Locator("#result")).ToHaveTextAsync("Alpha, Bravo, Charlie");
    }

    [TestMethod]
    [Ignore("Requires a route-capable origin; FulfillAsync cannot intercept on about:blank/data: URIs")]
    public async Task RouteAsync_Returns404()
    {
        await Page.RouteAsync("**/api/data", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 404,
                ContentType = "text/plain",
                Body = "Not Found"
            });
        });

        await Fixtures.SetPageContentViaBlankAsync(Page, Fixtures.ApiPage);
        await Page.Locator("#fetch-btn").ClickAsync();

        await Expect.That(Page.Locator("#error")).ToContainTextAsync("Error: 404");
    }

    [TestMethod]
    public async Task RouteAsync_AbortRequest()
    {
        // AbortAsync simulates a network failure
        await Page.RouteAsync("**/api/data", async route =>
        {
            await route.AbortAsync();
        });

        await Fixtures.SetPageContentViaBlankAsync(Page, Fixtures.ApiPage);
        await Page.Locator("#fetch-btn").ClickAsync();

        await Expect.That(Page.Locator("#error")).ToHaveTextAsync("Network error");
    }

    [TestMethod]
    [Ignore("Requires a route-capable origin; FulfillAsync cannot intercept on about:blank/data: URIs")]
    public async Task WaitForResponseAsync_CapturesResponse()
    {
        await Page.RouteAsync("**/api/data", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"items": ["X"]}"""
            });
        });

        await Fixtures.SetPageContentViaBlankAsync(Page, Fixtures.ApiPage);

        // WaitForResponseAsync captures the response matching the URL pattern
        var responseTask = Page.WaitForResponseAsync("**/api/data");
        await Page.Locator("#fetch-btn").ClickAsync();
        var response = await responseTask;

        // Expect.That(IResponse) provides status assertions
        await Expect.That(response).ToBeOkAsync();
        await Expect.That(response).ToHaveStatusAsync(200);
    }
}
