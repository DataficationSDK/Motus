using Motus.Abstractions;
using Motus.Cli.Services.Reporters;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class CoverageConsoleReporterTests
{
    private static CoverageData MakeData(double linePct, double cssPct, params (string Url, double Pct)[] files)
    {
        var scripts = files
            .Where(f => f.Url.EndsWith(".js"))
            .Select(f => new ScriptCoverage(f.Url, "x", Array.Empty<CoverageRange>(), new FileCoverageStats(100, (int)f.Pct, f.Pct)))
            .ToList();
        var sheets = files
            .Where(f => f.Url.EndsWith(".css"))
            .Select(f => new StylesheetCoverage(f.Url, "x", Array.Empty<CssRuleUsage>(), new FileCoverageStats(100, (int)f.Pct, f.Pct)))
            .ToList();
        return new CoverageData(scripts, sheets,
            new CoverageSummary(100, (int)linePct, linePct, 100, (int)cssPct, cssPct), DateTime.UtcNow);
    }

    [TestMethod]
    public async Task RunEnd_PrintsFileTableAndSummary()
    {
        var sw = new StringWriter();
        var reporter = new CoverageConsoleReporter(sw, useColor: false);

        var data = MakeData(75, 60, ("/app.js", 80), ("/main.css", 60));
        await reporter.OnCoverageRunEndAsync(data);

        var output = sw.ToString();
        StringAssert.Contains(output, "Coverage Summary");
        StringAssert.Contains(output, "/app.js");
        StringAssert.Contains(output, "/main.css");
        StringAssert.Contains(output, "Overall JS:");
        StringAssert.Contains(output, "Overall CSS:");
        StringAssert.Contains(output, "75.0%");
        StringAssert.Contains(output, "60.0%");
    }

    [TestMethod]
    public async Task RunEnd_NoData_PrintsNoneMessage()
    {
        var sw = new StringWriter();
        var reporter = new CoverageConsoleReporter(sw, useColor: false);

        await reporter.OnCoverageRunEndAsync(MakeData(0, 0));

        StringAssert.Contains(sw.ToString(), "no coverage data collected");
    }

    [TestMethod]
    public async Task RunEnd_UsesColor_WhenEnabled()
    {
        var sw = new StringWriter();
        var reporter = new CoverageConsoleReporter(sw, useColor: true);

        var data = MakeData(90, 90, ("/app.js", 90));
        await reporter.OnCoverageRunEndAsync(data);

        // Green ANSI escape for high coverage
        StringAssert.Contains(sw.ToString(), "\x1b[32m");
    }

    [TestMethod]
    public async Task PerTestCallback_DoesNotPrint()
    {
        var sw = new StringWriter();
        var reporter = new CoverageConsoleReporter(sw, useColor: false);

        var data = MakeData(50, 50, ("/a.js", 50));
        await reporter.OnCoverageCollectedAsync(data, new TestInfo("Test", "Suite"));

        Assert.AreEqual(string.Empty, sw.ToString());
    }
}
