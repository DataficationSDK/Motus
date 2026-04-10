namespace Motus.Samples.Tests;

/// <summary>
/// Performance testing showcase: budget assertions, individual metric assertions,
/// the [PerformanceBudget] attribute, and .Not negation. Uses inline HTML fixtures
/// that trigger real navigations so the performance metrics collector fires.
/// </summary>
[TestClass]
[PerformanceBudget(Lcp = 5000, Fcp = 5000, Cls = 1.0)]
public class PerformanceTests : MotusTestBase
{
    private const string SimplePage = """
        <!DOCTYPE html>
        <html lang="en">
        <head><title>Performance Sample</title></head>
        <body>
            <main>
                <h1>Performance Test Page</h1>
                <p>A lightweight page for budget validation.</p>
                <img src="data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7" alt="pixel" />
            </main>
        </body>
        </html>
        """;

    [TestMethod]
    public async Task SimplePage_MeetsPerformanceBudget()
    {
        await Fixtures.SetPageContentAsync(Page, SimplePage);
        await Expect.That(Page).ToMeetPerformanceBudgetAsync();
    }

    [TestMethod]
    [PerformanceBudget(Lcp = 10000)]
    public async Task MethodAttribute_OverridesClassBudget()
    {
        // The method-level budget (LCP = 10000ms) overrides the class-level budget.
        await Fixtures.SetPageContentAsync(Page, SimplePage);
        await Expect.That(Page).ToMeetPerformanceBudgetAsync();
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task LcpBelow_IndividualMetricAssertion()
    {
        // Individual metric assertions require a real HTTP origin for Web Vitals to fire.
        await Page.GotoAsync("https://example.com");
        await Expect.That(Page).ToHaveLcpBelowAsync(5000);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task FcpBelow_IndividualMetricAssertion()
    {
        await Page.GotoAsync("https://example.com");
        await Expect.That(Page).ToHaveFcpBelowAsync(5000);
    }

    [TestMethod]
    [TestCategory("Integration")]
    public async Task ClsBelow_NoLayoutShift()
    {
        await Page.GotoAsync("https://example.com");
        await Expect.That(Page).ToHaveClsBelowAsync(0.5);
    }

    [TestMethod]
    public async Task Not_LcpBelow_NegationAssertsThatLcpIsAtLeastThreshold()
    {
        await Fixtures.SetPageContentAsync(Page, SimplePage);
        // A 1x1 inline page will have LCP well above 0ms
        await Expect.That(Page).Not.ToHaveLcpBelowAsync(0);
    }
}
