using Motus.Abstractions;

namespace Motus.Tests.Coverage;

[TestClass]
public class CoverageThresholdTests
{
    private static CoverageData MakeData(double linePct, double cssPct) =>
        new CoverageData(
            Array.Empty<ScriptCoverage>(),
            Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(100, (int)linePct, linePct, 100, (int)cssPct, cssPct),
            DateTime.UtcNow);

    [TestMethod]
    public void NoThresholds_PassesWithEmptyEntries()
    {
        var result = CoverageThresholds.Evaluate(MakeData(10, 10), new CoverageOptions());
        Assert.IsTrue(result.Passed);
        Assert.AreEqual(0, result.Entries.Count);
    }

    [TestMethod]
    public void JsLineThreshold_PassesWhenAtOrAbove()
    {
        var opts = new CoverageOptions { JsLineThreshold = 80 };
        var result = CoverageThresholds.Evaluate(MakeData(85, 0), opts);
        Assert.IsTrue(result.Passed);
        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreEqual("js.lines", result.Entries[0].MetricName);
        Assert.IsTrue(result.Entries[0].Passed);
    }

    [TestMethod]
    public void JsLineThreshold_FailsWhenBelow()
    {
        var opts = new CoverageOptions { JsLineThreshold = 80 };
        var result = CoverageThresholds.Evaluate(MakeData(60, 0), opts);
        Assert.IsFalse(result.Passed);
        Assert.AreEqual(1, result.Failed.Count());
    }

    [TestMethod]
    public void CssRuleThreshold_FailsWhenBelow()
    {
        var opts = new CoverageOptions { CssRuleThreshold = 50 };
        var result = CoverageThresholds.Evaluate(MakeData(0, 30), opts);
        Assert.IsFalse(result.Passed);
        Assert.AreEqual("css.rules", result.Failed.First().MetricName);
    }

    [TestMethod]
    public void BothThresholds_OneFailsBothReported()
    {
        var opts = new CoverageOptions { JsLineThreshold = 60, CssRuleThreshold = 50 };
        var result = CoverageThresholds.Evaluate(MakeData(70, 30), opts);
        Assert.IsFalse(result.Passed);
        Assert.AreEqual(2, result.Entries.Count);
        Assert.AreEqual(1, result.Failed.Count());
    }

    [TestMethod]
    public void ExactBoundary_Passes()
    {
        var opts = new CoverageOptions { JsLineThreshold = 80 };
        var result = CoverageThresholds.Evaluate(MakeData(80, 0), opts);
        Assert.IsTrue(result.Passed);
    }
}
