using System.Xml.Linq;
using Motus.Abstractions;
using Motus.Cli.Services.Reporters;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class CoberturaReporterTests
{
    private string _outputPath = null!;

    [TestInitialize]
    public void Setup()
    {
        var dir = Path.Combine(Path.GetTempPath(), "motus-cob-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        _outputPath = Path.Combine(dir, "coverage.xml");
    }

    [TestCleanup]
    public void Cleanup()
    {
        var dir = Path.GetDirectoryName(_outputPath);
        if (dir is not null && Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    [TestMethod]
    public async Task RunEnd_WritesValidXmlWithCoverageRoot()
    {
        var reporter = new CoberturaReporter(_outputPath);
        var script = new ScriptCoverage("https://example.com/app.js",
            "var a = 1;\nvar b = 2;\n",
            new[] { new CoverageRange(0, 10, 1) },
            new FileCoverageStats(2, 1, 50));
        var data = new CoverageData(new[] { script }, Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(2, 1, 50, 0, 0, 0), DateTime.UtcNow);

        await reporter.OnCoverageRunEndAsync(data);

        Assert.IsTrue(File.Exists(_outputPath));
        var doc = XDocument.Load(_outputPath);
        Assert.IsNotNull(doc.Root);
        Assert.AreEqual("coverage", doc.Root!.Name.LocalName);

        Assert.AreEqual("0.5000", doc.Root.Attribute("line-rate")?.Value);
        Assert.AreEqual("1", doc.Root.Attribute("lines-covered")?.Value);
        Assert.AreEqual("2", doc.Root.Attribute("lines-valid")?.Value);
    }

    [TestMethod]
    public async Task RunEnd_GroupsByHostIntoPackages()
    {
        var reporter = new CoberturaReporter(_outputPath);
        var s1 = new ScriptCoverage("https://a.com/x.js", "a;\n", Array.Empty<CoverageRange>(), new FileCoverageStats(1, 0, 0));
        var s2 = new ScriptCoverage("https://b.com/y.js", "b;\n", Array.Empty<CoverageRange>(), new FileCoverageStats(1, 1, 100));
        var data = new CoverageData(new[] { s1, s2 }, Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(2, 1, 50, 0, 0, 0), DateTime.UtcNow);

        await reporter.OnCoverageRunEndAsync(data);

        var doc = XDocument.Load(_outputPath);
        var packages = doc.Root!.Element("packages")!.Elements("package").ToList();
        Assert.AreEqual(2, packages.Count);
        Assert.IsTrue(packages.Any(p => p.Attribute("name")?.Value == "a.com"));
        Assert.IsTrue(packages.Any(p => p.Attribute("name")?.Value == "b.com"));
    }

    [TestMethod]
    public async Task RunEnd_EmitsLineNumbersForScripts()
    {
        var reporter = new CoberturaReporter(_outputPath);
        var script = new ScriptCoverage("https://x/app.js",
            "alpha;\nbeta;\ngamma;\n",
            new[] { new CoverageRange(0, 6, 3), new CoverageRange(13, 19, 1) },
            new FileCoverageStats(3, 2, 66.7));
        var data = new CoverageData(new[] { script }, Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(3, 2, 66.7, 0, 0, 0), DateTime.UtcNow);

        await reporter.OnCoverageRunEndAsync(data);

        var doc = XDocument.Load(_outputPath);
        var lines = doc.Descendants("line").ToList();
        Assert.IsTrue(lines.Count >= 3);
        // Line 1 covered (count 3), line 2 not covered, line 3 covered (count 1)
        var byNumber = lines.ToDictionary(l => int.Parse(l.Attribute("number")!.Value), l => int.Parse(l.Attribute("hits")!.Value));
        Assert.AreEqual(3, byNumber[1]);
        Assert.AreEqual(0, byNumber[2]);
        Assert.AreEqual(1, byNumber[3]);
    }

    [TestMethod]
    public async Task RunEnd_EmptyData_StillWritesValidStructure()
    {
        var reporter = new CoberturaReporter(_outputPath);
        var data = new CoverageData(Array.Empty<ScriptCoverage>(), Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(0, 0, 0, 0, 0, 0), DateTime.UtcNow);

        await reporter.OnCoverageRunEndAsync(data);

        var doc = XDocument.Load(_outputPath);
        Assert.AreEqual("coverage", doc.Root!.Name.LocalName);
        Assert.IsNotNull(doc.Root.Element("packages"));
    }
}
