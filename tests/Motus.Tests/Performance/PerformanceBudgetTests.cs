using Motus.Abstractions;

namespace Motus.Tests.Performance;

[TestClass]
public class PerformanceBudgetTests
{
    [TestMethod]
    public void Evaluate_AllMetricsWithinBudget_Passes()
    {
        var budget = new PerformanceBudget
        {
            Lcp = 2500,
            Fcp = 1800,
            Cls = 0.1,
            Inp = 200,
            JsHeapSize = 50_000_000,
            DomNodeCount = 1500
        };

        var metrics = new PerformanceMetrics(
            Lcp: 2000, Fcp: 1500, Ttfb: 100, Cls: 0.05, Inp: 150,
            JsHeapSize: 40_000_000, DomNodeCount: 1200,
            LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        var result = budget.Evaluate(metrics);

        Assert.IsTrue(result.Passed);
        Assert.IsTrue(result.Entries.All(e => e.Passed));
    }

    [TestMethod]
    public void Evaluate_LcpExceedsBudget_Fails()
    {
        var budget = new PerformanceBudget { Lcp = 2500 };

        var metrics = new PerformanceMetrics(
            Lcp: 3000, Fcp: null, Ttfb: null, Cls: null, Inp: null,
            JsHeapSize: null, DomNodeCount: null,
            LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        var result = budget.Evaluate(metrics);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreEqual("LCP", result.Entries[0].MetricName);
        Assert.IsFalse(result.Entries[0].Passed);
        Assert.AreEqual(500, result.Entries[0].Delta);
    }

    [TestMethod]
    public void Evaluate_NullMetric_PassesWhenThresholdSet()
    {
        var budget = new PerformanceBudget { Lcp = 2500, Cls = 0.1 };

        var metrics = new PerformanceMetrics(
            Lcp: null, Fcp: null, Ttfb: null, Cls: null, Inp: null,
            JsHeapSize: null, DomNodeCount: null,
            LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        var result = budget.Evaluate(metrics);

        Assert.IsTrue(result.Passed, "Null metrics should pass budget checks.");
        Assert.AreEqual(2, result.Entries.Count);
        Assert.IsTrue(result.Entries.All(e => e.Passed));
        Assert.IsNull(result.Entries[0].Delta);
    }

    [TestMethod]
    public void Evaluate_NoThresholdsConfigured_EmptyResult()
    {
        var budget = new PerformanceBudget();

        var metrics = new PerformanceMetrics(
            Lcp: 5000, Fcp: 3000, Ttfb: 500, Cls: 1.0, Inp: 1000,
            JsHeapSize: 100_000_000, DomNodeCount: 5000,
            LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        var result = budget.Evaluate(metrics);

        Assert.IsTrue(result.Passed, "No thresholds should always pass.");
        Assert.AreEqual(0, result.Entries.Count);
    }

    [TestMethod]
    public void Evaluate_ExactlyAtThreshold_Passes()
    {
        var budget = new PerformanceBudget { Lcp = 2500 };

        var metrics = new PerformanceMetrics(
            Lcp: 2500, Fcp: null, Ttfb: null, Cls: null, Inp: null,
            JsHeapSize: null, DomNodeCount: null,
            LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        var result = budget.Evaluate(metrics);

        Assert.IsTrue(result.Passed, "Metric exactly at threshold should pass.");
    }

    [TestMethod]
    public void Evaluate_MultipleFailures_ReportsAll()
    {
        var budget = new PerformanceBudget { Lcp = 2500, Cls = 0.1 };

        var metrics = new PerformanceMetrics(
            Lcp: 3000, Fcp: null, Ttfb: null, Cls: 0.5, Inp: null,
            JsHeapSize: null, DomNodeCount: null,
            LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        var result = budget.Evaluate(metrics);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(2, result.Entries.Count);
        Assert.IsTrue(result.Entries.All(e => !e.Passed));
    }

    [TestMethod]
    public void Evaluate_JsHeapSize_CorrectDelta()
    {
        var budget = new PerformanceBudget { JsHeapSize = 50_000_000 };

        var metrics = new PerformanceMetrics(
            Lcp: null, Fcp: null, Ttfb: null, Cls: null, Inp: null,
            JsHeapSize: 60_000_000, DomNodeCount: null,
            LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        var result = budget.Evaluate(metrics);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual("JSHeapSize", result.Entries[0].MetricName);
        Assert.AreEqual(10_000_000, result.Entries[0].Delta);
    }

    [TestMethod]
    public void Evaluate_DomNodeCount_CorrectDelta()
    {
        var budget = new PerformanceBudget { DomNodeCount = 1000 };

        var metrics = new PerformanceMetrics(
            Lcp: null, Fcp: null, Ttfb: null, Cls: null, Inp: null,
            JsHeapSize: null, DomNodeCount: 800,
            LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        var result = budget.Evaluate(metrics);

        Assert.IsTrue(result.Passed);
        Assert.AreEqual(-200, result.Entries[0].Delta);
    }
}
