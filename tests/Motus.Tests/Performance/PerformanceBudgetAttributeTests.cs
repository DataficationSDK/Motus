using Motus.Abstractions;

namespace Motus.Tests.Performance;

[TestClass]
public class PerformanceBudgetAttributeTests
{
    [TestMethod]
    public void ToBudget_AllPropertiesSet_MapsCorrectly()
    {
        var attr = new PerformanceBudgetAttribute
        {
            Lcp = 2500,
            Fcp = 1800,
            Ttfb = 600,
            Cls = 0.1,
            Inp = 200,
            JsHeapSize = 50_000_000,
            DomNodeCount = 1500,
        };

        var budget = attr.ToBudget();

        Assert.AreEqual(2500, budget.Lcp);
        Assert.AreEqual(1800, budget.Fcp);
        Assert.AreEqual(600, budget.Ttfb);
        Assert.AreEqual(0.1, budget.Cls);
        Assert.AreEqual(200, budget.Inp);
        Assert.AreEqual(50_000_000, budget.JsHeapSize);
        Assert.AreEqual(1500, budget.DomNodeCount);
    }

    [TestMethod]
    public void ToBudget_DefaultSentinels_ProduceNullThresholds()
    {
        var attr = new PerformanceBudgetAttribute();

        var budget = attr.ToBudget();

        Assert.IsNull(budget.Lcp);
        Assert.IsNull(budget.Fcp);
        Assert.IsNull(budget.Ttfb);
        Assert.IsNull(budget.Cls);
        Assert.IsNull(budget.Inp);
        Assert.IsNull(budget.JsHeapSize);
        Assert.IsNull(budget.DomNodeCount);
    }

    [TestMethod]
    public void ToBudget_PartialProperties_OnlySetOnesPopulated()
    {
        var attr = new PerformanceBudgetAttribute
        {
            Lcp = 2500,
            Cls = 0.1,
        };

        var budget = attr.ToBudget();

        Assert.AreEqual(2500, budget.Lcp);
        Assert.IsNull(budget.Fcp);
        Assert.IsNull(budget.Ttfb);
        Assert.AreEqual(0.1, budget.Cls);
        Assert.IsNull(budget.Inp);
        Assert.IsNull(budget.JsHeapSize);
        Assert.IsNull(budget.DomNodeCount);
    }

    [TestMethod]
    public void ToBudget_ZeroIsValidThreshold()
    {
        var attr = new PerformanceBudgetAttribute { Cls = 0 };

        var budget = attr.ToBudget();

        Assert.AreEqual(0.0, budget.Cls);
    }
}
