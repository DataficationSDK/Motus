using Motus.Abstractions;

namespace Motus.Tests.Coverage;

[TestClass]
public class CoverageSinkTests
{
    private static CoverageData MakeData(int totalLines) =>
        new(
            Scripts: Array.Empty<ScriptCoverage>(),
            Stylesheets: Array.Empty<StylesheetCoverage>(),
            Summary: new CoverageSummary(totalLines, 0, 0, 0, 0, 0),
            CollectedAtUtc: DateTime.UtcNow);

    [TestMethod]
    public void Begin_Add_End_ReturnsLastData()
    {
        CoverageSink.Begin();
        CoverageSink.Add(MakeData(10));
        CoverageSink.Add(MakeData(20));
        var result = CoverageSink.End();

        Assert.IsNotNull(result);
        Assert.AreEqual(20, result!.Summary.TotalLines);
    }

    [TestMethod]
    public void End_ClearsState()
    {
        CoverageSink.Begin();
        CoverageSink.Add(MakeData(5));
        CoverageSink.End();

        Assert.IsNull(CoverageSink.End());
    }

    [TestMethod]
    public async Task ParallelFlows_AreIsolated()
    {
        int? a = null, b = null;

        var t1 = Task.Run(() =>
        {
            CoverageSink.Begin();
            CoverageSink.Add(MakeData(111));
            a = CoverageSink.End()?.Summary.TotalLines;
        });

        var t2 = Task.Run(() =>
        {
            CoverageSink.Begin();
            CoverageSink.Add(MakeData(222));
            b = CoverageSink.End()?.Summary.TotalLines;
        });

        await Task.WhenAll(t1, t2);
        Assert.AreEqual(111, a);
        Assert.AreEqual(222, b);
    }
}
