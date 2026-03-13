namespace Motus.Samples.Tests;

/// <summary>
/// Phase 2C: Network interception with RouteAsync, FulfillAsync, AbortAsync, and WaitForResponseAsync.
/// </summary>
[TestClass]
public class NetworkMockingTests : MotusTestBase
{
    [TestMethod]
    public async Task RouteAsync_FulfillsWithJson()
    {
        await Page.SetContentAsync(Fixtures.ApiPage);

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

        await Page.Locator("#fetch-btn").ClickAsync();

        await Expect.That(Page.Locator("#result")).ToHaveTextAsync("Alpha, Bravo, Charlie");
    }

    [TestMethod]
    public async Task RouteAsync_Returns404()
    {
        await Page.SetContentAsync(Fixtures.ApiPage);

        await Page.RouteAsync("**/api/data", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 404,
                ContentType = "text/plain",
                Body = "Not Found"
            });
        });

        await Page.Locator("#fetch-btn").ClickAsync();

        await Expect.That(Page.Locator("#error")).ToContainTextAsync("Error: 404");
    }

    [TestMethod]
    public async Task RouteAsync_AbortRequest()
    {
        await Page.SetContentAsync(Fixtures.ApiPage);

        // AbortAsync simulates a network failure
        await Page.RouteAsync("**/api/data", async route =>
        {
            await route.AbortAsync();
        });

        await Page.Locator("#fetch-btn").ClickAsync();

        await Expect.That(Page.Locator("#error")).ToHaveTextAsync("Network error");
    }

    [TestMethod]
    public async Task WaitForResponseAsync_CapturesResponse()
    {
        await Page.SetContentAsync(Fixtures.ApiPage);

        await Page.RouteAsync("**/api/data", async route =>
        {
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"items": ["X"]}"""
            });
        });

        // WaitForResponseAsync captures the response matching the URL pattern
        var responseTask = Page.WaitForResponseAsync("**/api/data");
        await Page.Locator("#fetch-btn").ClickAsync();
        var response = await responseTask;

        // Expect.That(IResponse) provides status assertions
        await Expect.That(response).ToBeOkAsync();
        await Expect.That(response).ToHaveStatusAsync(200);
    }
}
