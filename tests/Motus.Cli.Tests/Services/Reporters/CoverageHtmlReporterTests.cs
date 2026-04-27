using Motus.Abstractions;
using Motus.Cli.Services.Reporters;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class CoverageHtmlReporterTests
{
    private string _outputDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _outputDir = Path.Combine(Path.GetTempPath(), "motus-cov-tests-" + Guid.NewGuid().ToString("N")[..8]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_outputDir))
            Directory.Delete(_outputDir, recursive: true);
    }

    [TestMethod]
    public async Task RunEnd_WritesIndexHtml()
    {
        var reporter = new CoverageHtmlReporter(_outputDir);
        var script = new ScriptCoverage("/app.js", "var x = 1;\nvar y = 2;\n",
            new[] { new CoverageRange(0, 10, 1) },
            new FileCoverageStats(2, 1, 50));
        var data = new CoverageData(new[] { script }, Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(2, 1, 50, 0, 0, 0), DateTime.UtcNow);

        await reporter.OnCoverageRunEndAsync(data);

        var indexPath = Path.Combine(_outputDir, "index.html");
        Assert.IsTrue(File.Exists(indexPath));
        var html = await File.ReadAllTextAsync(indexPath);
        StringAssert.Contains(html, "Coverage Report");
        StringAssert.Contains(html, "/app.js");
        StringAssert.Contains(html, "50.0%");
    }

    [TestMethod]
    public async Task RunEnd_WritesPerFilePages()
    {
        var reporter = new CoverageHtmlReporter(_outputDir);
        var script = new ScriptCoverage("https://example/app.js", "function foo() {}\n",
            new[] { new CoverageRange(0, 17, 1) },
            new FileCoverageStats(1, 1, 100));
        var data = new CoverageData(new[] { script }, Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(1, 1, 100, 0, 0, 0), DateTime.UtcNow);

        await reporter.OnCoverageRunEndAsync(data);

        var files = Directory.GetFiles(_outputDir, "js-*.html");
        Assert.AreEqual(1, files.Length);
        var html = await File.ReadAllTextAsync(files[0]);
        StringAssert.Contains(html, "function foo()");
        StringAssert.Contains(html, "covered");
    }

    [TestMethod]
    public async Task RunEnd_StylesheetPage_WrittenWithCssPrefix()
    {
        var reporter = new CoverageHtmlReporter(_outputDir);
        var sheet = new StylesheetCoverage("/styles.css", ".a { color: red; }\n",
            new[] { new CssRuleUsage(0, 18, true) },
            new FileCoverageStats(1, 1, 100));
        var data = new CoverageData(Array.Empty<ScriptCoverage>(), new[] { sheet },
            new CoverageSummary(0, 0, 0, 1, 1, 100), DateTime.UtcNow);

        await reporter.OnCoverageRunEndAsync(data);

        Assert.AreEqual(1, Directory.GetFiles(_outputDir, "css-*.html").Length);
    }

    [TestMethod]
    public async Task RunEnd_EmptyData_WritesIndexWithGaugesOnly()
    {
        var reporter = new CoverageHtmlReporter(_outputDir);
        var data = new CoverageData(Array.Empty<ScriptCoverage>(), Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(0, 0, 0, 0, 0, 0), DateTime.UtcNow);

        await reporter.OnCoverageRunEndAsync(data);

        Assert.IsTrue(File.Exists(Path.Combine(_outputDir, "index.html")));
    }
}
