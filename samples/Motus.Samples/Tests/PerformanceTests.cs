namespace Motus.Samples.Tests;

/// <summary>
/// Performance testing showcase: budget assertions, individual metric
/// assertions (LCP, FCP, TTFB, CLS), the [PerformanceBudget] attribute with
/// class/method override, and .Not negation.
///
/// Uses real HTTP navigations so the PerformanceObserver collects actual web vitals.
/// When run in the visual runner, navigate steps appear as annotated timeline markers
/// with LCP, FCP, and CLS values, and the step detail panel shows a Performance section.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[PerformanceBudget(Lcp = 5000, Fcp = 5000, Cls = 1.0)]
public class PerformanceTests : MotusTestBase
{
    private const string ExampleUrl = "https://example.com";

    [TestMethod]
    public async Task ExampleDotCom_MeetsPerformanceBudget()
    {
        await Page.GotoAsync(ExampleUrl);
        await Expect.That(Page).ToMeetPerformanceBudgetAsync();
    }

    [TestMethod]
    [PerformanceBudget(Lcp = 10000)]
    public async Task MethodAttribute_OverridesClassBudget()
    {
        // The method-level budget (LCP = 10000ms) overrides the class-level budget.
        await Page.GotoAsync(ExampleUrl);
        await Expect.That(Page).ToMeetPerformanceBudgetAsync();
    }

    [TestMethod]
    public async Task LcpBelow_IndividualMetricAssertion()
    {
        await Page.GotoAsync(ExampleUrl);
        await Expect.That(Page).ToHaveLcpBelowAsync(5000);
    }

    [TestMethod]
    public async Task FcpBelow_IndividualMetricAssertion()
    {
        await Page.GotoAsync(ExampleUrl);
        await Expect.That(Page).ToHaveFcpBelowAsync(5000);
    }

    [TestMethod]
    public async Task TtfbBelow_ServerResponseTime()
    {
        await Page.GotoAsync(ExampleUrl);
        await Expect.That(Page).ToHaveTtfbBelowAsync(3000);
    }

    [TestMethod]
    public async Task ClsBelow_NoLayoutShift()
    {
        await Page.GotoAsync(ExampleUrl);
        await Expect.That(Page).ToHaveClsBelowAsync(0.5);
    }

    [TestMethod]
    public async Task Not_LcpBelow_NegationAssertsThatLcpIsAtLeastThreshold()
    {
        await Page.GotoAsync(ExampleUrl);
        // A real page will have LCP well above 0ms
        await Expect.That(Page).Not.ToHaveLcpBelowAsync(0);
    }

    [TestMethod]
    public async Task MultipleNavigations_EachProducesTimelineMetrics()
    {
        // Each GotoAsync fires the PerformanceMetricsCollector, creating a
        // separate annotated navigate marker on the visual runner timeline.
        await Page.GotoAsync(ExampleUrl);
        await Expect.That(Page).ToHaveLcpBelowAsync(5000);

        await Page.GotoAsync("https://www.iana.org/domains/reserved");
        await Expect.That(Page).ToMeetPerformanceBudgetAsync();
    }
}
