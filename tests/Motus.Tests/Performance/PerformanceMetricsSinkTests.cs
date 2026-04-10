using Motus.Abstractions;

namespace Motus.Tests.Performance;

[TestClass]
public class PerformanceMetricsSinkTests
{
    private static PerformanceMetrics MakeMetrics(double lcp) =>
        new(Lcp: lcp, Fcp: null, Ttfb: null, Cls: null, Inp: null,
            JsHeapSize: null, DomNodeCount: null, LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

    [TestMethod]
    public void Begin_Add_End_ReturnsLastMetrics()
    {
        PerformanceMetricsSink.Begin();
        PerformanceMetricsSink.Add(MakeMetrics(1000));
        PerformanceMetricsSink.Add(MakeMetrics(2000));
        var result = PerformanceMetricsSink.End();

        Assert.IsNotNull(result);
        Assert.AreEqual(2000, result!.Lcp);
    }

    [TestMethod]
    public void End_WithoutBegin_ReturnsNull()
    {
        var result = PerformanceMetricsSink.End();
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Begin_End_WithNoAdd_ReturnsNull()
    {
        PerformanceMetricsSink.Begin();
        var result = PerformanceMetricsSink.End();
        Assert.IsNull(result);
    }

    [TestMethod]
    public void End_ClearsState()
    {
        PerformanceMetricsSink.Begin();
        PerformanceMetricsSink.Add(MakeMetrics(1000));
        PerformanceMetricsSink.End();

        var result = PerformanceMetricsSink.End();
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ParallelFlows_AreIsolated()
    {
        double? lcp1 = null;
        double? lcp2 = null;

        var task1 = Task.Run(() =>
        {
            PerformanceMetricsSink.Begin();
            PerformanceMetricsSink.Add(MakeMetrics(1111));
            var result = PerformanceMetricsSink.End();
            lcp1 = result?.Lcp;
        });

        var task2 = Task.Run(() =>
        {
            PerformanceMetricsSink.Begin();
            PerformanceMetricsSink.Add(MakeMetrics(2222));
            var result = PerformanceMetricsSink.End();
            lcp2 = result?.Lcp;
        });

        await Task.WhenAll(task1, task2);

        Assert.AreEqual(1111, lcp1);
        Assert.AreEqual(2222, lcp2);
    }
}
