using Motus.Abstractions;

namespace Motus.Tests.Coverage;

[TestClass]
public class CoverageRemapperTests
{
    /// <summary>
    /// Synthetic map: generated source has two lines.
    /// Line 0 maps to a.ts:0; line 1 maps to b.ts:0.
    /// </summary>
    private static SourceMap BuildTwoFileMap()
    {
        return new SourceMap(
            Version: 3,
            Sources: new[] { "a.ts", "b.ts" },
            SourcesContent: new string?[] { "alpha\nbeta\n", "gamma\ndelta\n" },
            Lines: new[]
            {
                new MappingLine(new[] { new MappingSegment(0, 0, 0, 0, null) }),
                new MappingLine(new[] { new MappingSegment(0, 1, 0, 0, null) }),
            },
            SourceRoot: null);
    }

    [TestMethod]
    public void Remap_RangeCoversBothLines_EmitsBothFiles()
    {
        var generated = "GENA\nGENB\n";  // 5 + 5 = 10 chars; line 0 = [0,4], line 1 = [5,9]
        var ranges = new[] { new CoverageRange(0, generated.Length, 1) };
        var map = BuildTwoFileMap();

        var result = CoverageRemapper.Remap(generated, ranges, map);

        Assert.AreEqual(2, result.Count);
        var paths = result.Select(r => r.OriginalPath).OrderBy(p => p).ToArray();
        CollectionAssert.AreEqual(new[] { "a.ts", "b.ts" }, paths);

        foreach (var f in result)
        {
            Assert.IsNotNull(f.OriginalSource);
            Assert.AreEqual(1, f.Ranges.Count, $"{f.OriginalPath} should have one covered range.");
            Assert.IsTrue(f.Stats.CoveredLines >= 1);
        }
    }

    [TestMethod]
    public void Remap_RangeOnFirstLineOnly_EmitsOnlyFirstFile()
    {
        var generated = "GENA\nGENB\n";
        var ranges = new[] { new CoverageRange(0, 4, 1) };
        var map = BuildTwoFileMap();

        var result = CoverageRemapper.Remap(generated, ranges, map);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("a.ts", result[0].OriginalPath);
    }

    [TestMethod]
    public void Remap_NoSourcesContent_ProducesLineMarkerRanges()
    {
        var generated = "GENA\nGENB\n";
        var ranges = new[] { new CoverageRange(0, generated.Length, 5) };
        var map = new SourceMap(
            Version: 3,
            Sources: new[] { "a.ts", "b.ts" },
            SourcesContent: Array.Empty<string?>(),
            Lines: new[]
            {
                new MappingLine(new[] { new MappingSegment(0, 0, 7, 0, null) }),
                new MappingLine(new[] { new MappingSegment(0, 1, 12, 0, null) }),
            },
            SourceRoot: null);

        var result = CoverageRemapper.Remap(generated, ranges, map);

        Assert.AreEqual(2, result.Count);
        foreach (var f in result)
        {
            Assert.IsNull(f.OriginalSource);
            Assert.AreEqual(1, f.Ranges.Count);
            // Synthetic line-marker range: [line, line+1)
            var r = f.Ranges[0];
            Assert.AreEqual(1, r.EndOffset - r.StartOffset);
            Assert.AreEqual(5, r.Count);
            Assert.AreEqual(1, f.Stats.CoveredLines);
        }
    }

    [TestMethod]
    public void Remap_ZeroCountRange_StillRecordsButCountStaysZero()
    {
        var generated = "GENA\nGENB\n";
        var ranges = new[] { new CoverageRange(0, generated.Length, 0) };
        var map = BuildTwoFileMap();

        var result = CoverageRemapper.Remap(generated, ranges, map);

        Assert.AreEqual(2, result.Count);
        foreach (var f in result)
        {
            Assert.AreEqual(0, f.Ranges.Sum(r => r.Count));
        }
    }

    [TestMethod]
    public void Remap_NoMappingForRange_ReturnsEmpty()
    {
        var generated = "GENA\nGENB\n";
        var ranges = new[] { new CoverageRange(0, 4, 1) };
        var map = new SourceMap(
            Version: 3,
            Sources: new[] { "a.ts" },
            SourcesContent: new string?[] { "x" },
            Lines: new[]
            {
                new MappingLine(Array.Empty<MappingSegment>()),
                new MappingLine(Array.Empty<MappingSegment>()),
            },
            SourceRoot: null);

        var result = CoverageRemapper.Remap(generated, ranges, map);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Remap_SourceRoot_IsPrependedToPath()
    {
        var generated = "X\n";
        var ranges = new[] { new CoverageRange(0, 1, 1) };
        var map = new SourceMap(
            Version: 3,
            Sources: new[] { "a.ts" },
            SourcesContent: new string?[] { "x\n" },
            Lines: new[] { new MappingLine(new[] { new MappingSegment(0, 0, 0, 0, null) }) },
            SourceRoot: "src/");

        var result = CoverageRemapper.Remap(generated, ranges, map);
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("src/a.ts", result[0].OriginalPath);
    }
}
