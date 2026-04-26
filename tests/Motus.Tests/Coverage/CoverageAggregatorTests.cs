using Motus.Abstractions;

namespace Motus.Tests.Coverage;

[TestClass]
public class CoverageAggregatorTests
{
    [TestMethod]
    public void MergeRanges_Empty_ReturnsEmpty()
    {
        var result = CoverageAggregator.MergeRanges(Array.Empty<CoverageRange>());
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void MergeRanges_Single_ReturnsSame()
    {
        var input = new[] { new CoverageRange(0, 10, 1) };
        var result = CoverageAggregator.MergeRanges(input);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(0, result[0].StartOffset);
        Assert.AreEqual(10, result[0].EndOffset);
        Assert.AreEqual(1, result[0].Count);
    }

    [TestMethod]
    public void MergeRanges_Disjoint_KeepsSeparate()
    {
        var input = new[]
        {
            new CoverageRange(0, 5, 1),
            new CoverageRange(10, 15, 2),
        };
        var result = CoverageAggregator.MergeRanges(input);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(0, result[0].StartOffset);
        Assert.AreEqual(5, result[0].EndOffset);
        Assert.AreEqual(1, result[0].Count);
        Assert.AreEqual(10, result[1].StartOffset);
        Assert.AreEqual(15, result[1].EndOffset);
        Assert.AreEqual(2, result[1].Count);
    }

    [TestMethod]
    public void MergeRanges_Adjacent_SameCount_Coalesces()
    {
        var input = new[]
        {
            new CoverageRange(0, 5, 1),
            new CoverageRange(5, 10, 1),
        };
        var result = CoverageAggregator.MergeRanges(input);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(0, result[0].StartOffset);
        Assert.AreEqual(10, result[0].EndOffset);
        Assert.AreEqual(1, result[0].Count);
    }

    [TestMethod]
    public void MergeRanges_Overlapping_SumsCounts()
    {
        var input = new[]
        {
            new CoverageRange(0, 10, 2),
            new CoverageRange(5, 15, 3),
        };
        var result = CoverageAggregator.MergeRanges(input);
        Assert.AreEqual(3, result.Count);
        Assert.AreEqual((0, 5, 2), (result[0].StartOffset, result[0].EndOffset, result[0].Count));
        Assert.AreEqual((5, 10, 5), (result[1].StartOffset, result[1].EndOffset, result[1].Count));
        Assert.AreEqual((10, 15, 3), (result[2].StartOffset, result[2].EndOffset, result[2].Count));
    }

    [TestMethod]
    public void MergeRanges_DegenerateRange_Skipped()
    {
        var input = new[]
        {
            new CoverageRange(5, 5, 1),
            new CoverageRange(0, 10, 2),
        };
        var result = CoverageAggregator.MergeRanges(input);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(2, result[0].Count);
    }

    [TestMethod]
    public void SummarizeScript_EmptySource_ReturnsZeroes()
    {
        var stats = CoverageAggregator.SummarizeScript(string.Empty, Array.Empty<CoverageRange>());
        Assert.AreEqual(0, stats.TotalLines);
        Assert.AreEqual(0, stats.CoveredLines);
        Assert.AreEqual(0, stats.Percentage);
    }

    [TestMethod]
    public void SummarizeScript_RangeCoversAllLines_FullCoverage()
    {
        var source = "line1\nline2\nline3";
        var ranges = new[] { new CoverageRange(0, source.Length, 1) };
        var stats = CoverageAggregator.SummarizeScript(source, ranges);
        Assert.AreEqual(3, stats.TotalLines);
        Assert.AreEqual(3, stats.CoveredLines);
        Assert.AreEqual(100.0, stats.Percentage);
    }

    [TestMethod]
    public void SummarizeScript_PartialCoverage_CountsCorrectLines()
    {
        // line0: "line1" (offsets 0..5), '\n' at 5
        // line1: "line2" (offsets 6..11), '\n' at 11
        // line2: "line3" (offsets 12..17)
        var source = "line1\nline2\nline3";
        var ranges = new[]
        {
            new CoverageRange(0, 5, 1),    // covers line 0
            new CoverageRange(12, 17, 1),  // covers line 2
        };
        var stats = CoverageAggregator.SummarizeScript(source, ranges);
        Assert.AreEqual(3, stats.TotalLines);
        Assert.AreEqual(2, stats.CoveredLines);
    }

    [TestMethod]
    public void SummarizeScript_ZeroCountRange_NotCovered()
    {
        var source = "line1\nline2";
        var ranges = new[] { new CoverageRange(0, source.Length, 0) };
        var stats = CoverageAggregator.SummarizeScript(source, ranges);
        Assert.AreEqual(2, stats.TotalLines);
        Assert.AreEqual(0, stats.CoveredLines);
    }

    [TestMethod]
    public void SummarizeScript_RangeSpansNewline_CoversBothLines()
    {
        // Range [3..9) covers end of line 0 (offsets 3..5) and start of line 1 (6..9)
        var source = "line1\nline2\nline3";
        var ranges = new[] { new CoverageRange(3, 9, 1) };
        var stats = CoverageAggregator.SummarizeScript(source, ranges);
        Assert.AreEqual(2, stats.CoveredLines, "Range crossing newline should mark both lines covered.");
    }

    [TestMethod]
    public void SummarizeScript_TrailingNewline_NotCountedAsExtraLine()
    {
        var source = "a\nb\n";
        var stats = CoverageAggregator.SummarizeScript(source, new[] { new CoverageRange(0, 4, 1) });
        Assert.AreEqual(2, stats.TotalLines);
        Assert.AreEqual(2, stats.CoveredLines);
    }

    [TestMethod]
    public void SummarizeStylesheet_EmptyRules_ReturnsZeroes()
    {
        var stats = CoverageAggregator.SummarizeStylesheet(Array.Empty<CssRuleUsage>());
        Assert.AreEqual(0, stats.TotalLines);
        Assert.AreEqual(0, stats.CoveredLines);
    }

    [TestMethod]
    public void MergeOriginalFiles_Empty_ReturnsEmpty()
    {
        var result = CoverageAggregator.MergeOriginalFiles(Array.Empty<OriginalFileCoverage>());
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void MergeOriginalFiles_SamePath_UnionsRanges()
    {
        var src = "a\nb\nc\n"; // three lines: [0,2), [2,4), [4,6)
        var a = new OriginalFileCoverage("x.ts", src,
            new[] { new CoverageRange(0, 2, 1) },
            new FileCoverageStats(3, 1, 33.3));
        var b = new OriginalFileCoverage("x.ts", src,
            new[] { new CoverageRange(4, 6, 1) },
            new FileCoverageStats(3, 1, 33.3));

        var result = CoverageAggregator.MergeOriginalFiles(new[] { a, b });

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("x.ts", result[0].OriginalPath);
        Assert.AreEqual(3, result[0].Stats.TotalLines);
        Assert.AreEqual(2, result[0].Stats.CoveredLines);
    }

    [TestMethod]
    public void MergeOriginalFiles_DifferentPaths_KeptSeparate()
    {
        var a = new OriginalFileCoverage("a.ts", "x\n",
            new[] { new CoverageRange(0, 1, 1) },
            new FileCoverageStats(1, 1, 100));
        var b = new OriginalFileCoverage("b.ts", "y\n",
            new[] { new CoverageRange(0, 1, 1) },
            new FileCoverageStats(1, 1, 100));

        var result = CoverageAggregator.MergeOriginalFiles(new[] { a, b });
        Assert.AreEqual(2, result.Count);
    }

    [TestMethod]
    public void MergeOriginalFiles_FirstHasNoSource_LaterFillsIt()
    {
        var withoutSource = new OriginalFileCoverage("x.ts", null,
            new[] { new CoverageRange(0, 1, 1) },
            new FileCoverageStats(1, 1, 100));
        var withSource = new OriginalFileCoverage("x.ts", "alpha\nbeta\n",
            new[] { new CoverageRange(0, 5, 1) },
            new FileCoverageStats(2, 1, 50));

        var result = CoverageAggregator.MergeOriginalFiles(new[] { withoutSource, withSource });
        Assert.AreEqual(1, result.Count);
        Assert.IsNotNull(result[0].OriginalSource);
        Assert.AreEqual(2, result[0].Stats.TotalLines);
    }

    [TestMethod]
    public void SummarizeStylesheet_MixedUsage_ComputesPercentage()
    {
        var rules = new[]
        {
            new CssRuleUsage(0, 10, true),
            new CssRuleUsage(10, 20, false),
            new CssRuleUsage(20, 30, true),
        };
        var stats = CoverageAggregator.SummarizeStylesheet(rules);
        Assert.AreEqual(3, stats.TotalLines);
        Assert.AreEqual(2, stats.CoveredLines);
        Assert.AreEqual(2.0 * 100.0 / 3.0, stats.Percentage, 0.0001);
    }

    [TestMethod]
    public void MergeScripts_SameUrlAcrossSnapshots_MergesRanges()
    {
        var source = "abcdefghij"; // 10 chars, single line
        var s1 = new ScriptCoverage("a.js", source,
            new[] { new CoverageRange(0, 5, 1) },
            new FileCoverageStats(0, 0, 0));
        var s2 = new ScriptCoverage("a.js", source,
            new[] { new CoverageRange(5, 10, 2) },
            new FileCoverageStats(0, 0, 0));

        var merged = CoverageAggregator.MergeScripts(new[] { s1, s2 });
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual("a.js", merged[0].Url);
        Assert.AreEqual(2, merged[0].Ranges.Count);
    }

    [TestMethod]
    public void MergeScripts_DifferentUrls_StayDistinct()
    {
        var s1 = new ScriptCoverage("a.js", "x", new[] { new CoverageRange(0, 1, 1) }, new FileCoverageStats(0, 0, 0));
        var s2 = new ScriptCoverage("b.js", "y", new[] { new CoverageRange(0, 1, 1) }, new FileCoverageStats(0, 0, 0));

        var merged = CoverageAggregator.MergeScripts(new[] { s1, s2 });
        Assert.AreEqual(2, merged.Count);
    }

    [TestMethod]
    public void MergeStylesheets_RuleUsedInOneSnapshot_ReportedAsUsed()
    {
        var s1 = new StylesheetCoverage("style.css", ".a{}",
            new[] { new CssRuleUsage(0, 4, false) },
            new FileCoverageStats(0, 0, 0));
        var s2 = new StylesheetCoverage("style.css", ".a{}",
            new[] { new CssRuleUsage(0, 4, true) },
            new FileCoverageStats(0, 0, 0));

        var merged = CoverageAggregator.MergeStylesheets(new[] { s1, s2 });
        Assert.AreEqual(1, merged.Count);
        Assert.AreEqual(1, merged[0].Rules.Count);
        Assert.IsTrue(merged[0].Rules[0].Used);
    }

    [TestMethod]
    public void BuildSummary_AggregatesAcrossFiles()
    {
        var scripts = new[]
        {
            new ScriptCoverage("a.js", "", Array.Empty<CoverageRange>(), new FileCoverageStats(10, 5, 50)),
            new ScriptCoverage("b.js", "", Array.Empty<CoverageRange>(), new FileCoverageStats(20, 15, 75)),
        };
        var stylesheets = new[]
        {
            new StylesheetCoverage("s.css", "", Array.Empty<CssRuleUsage>(), new FileCoverageStats(4, 2, 50)),
        };

        var summary = CoverageAggregator.BuildSummary(scripts, stylesheets);
        Assert.AreEqual(30, summary.TotalLines);
        Assert.AreEqual(20, summary.CoveredLines);
        Assert.AreEqual(20.0 * 100.0 / 30.0, summary.LinePercentage, 0.0001);
        Assert.AreEqual(4, summary.TotalCssRules);
        Assert.AreEqual(2, summary.UsedCssRules);
        Assert.AreEqual(50.0, summary.CssPercentage);
    }

    [TestMethod]
    public void BuildSummary_NoFiles_ReturnsZeroes()
    {
        var summary = CoverageAggregator.BuildSummary(Array.Empty<ScriptCoverage>(), Array.Empty<StylesheetCoverage>());
        Assert.AreEqual(0, summary.TotalLines);
        Assert.AreEqual(0, summary.LinePercentage);
        Assert.AreEqual(0, summary.CssPercentage);
    }
}
