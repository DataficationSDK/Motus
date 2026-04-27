using Motus.Abstractions;
using Motus.Runner.Services.Coverage;

namespace Motus.Tests.Runner;

[TestClass]
public class CoverageServiceTests
{
    [TestMethod]
    public void Latest_StartsNull_HasDataFalse()
    {
        var service = new CoverageService();
        Assert.IsNull(service.Latest);
        Assert.IsFalse(service.HasData);
    }

    [TestMethod]
    public void Set_StoresSnapshot_RaisesEvent()
    {
        var service = new CoverageService();
        var raised = 0;
        service.CoverageChanged += () => raised++;

        var data = MakeData(scriptCount: 1);
        service.Set(data);

        Assert.AreSame(data, service.Latest);
        Assert.IsTrue(service.HasData);
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void HasData_FalseForEmptySnapshot()
    {
        var service = new CoverageService();
        service.Set(MakeData(scriptCount: 0));
        Assert.IsFalse(service.HasData);
    }

    [TestMethod]
    public void Clear_ResetsAndRaises()
    {
        var service = new CoverageService();
        service.Set(MakeData(scriptCount: 1));
        var raised = 0;
        service.CoverageChanged += () => raised++;

        service.Clear();

        Assert.IsNull(service.Latest);
        Assert.IsFalse(service.HasData);
        Assert.AreEqual(1, raised);
    }

    private static CoverageData MakeData(int scriptCount)
    {
        var scripts = Enumerable.Range(0, scriptCount)
            .Select(i => new ScriptCoverage($"/s{i}.js", "x", Array.Empty<CoverageRange>(), new FileCoverageStats(1, 0, 0)))
            .ToList();
        return new CoverageData(
            scripts,
            Array.Empty<StylesheetCoverage>(),
            new CoverageSummary(scriptCount, 0, 0, 0, 0, 0),
            DateTime.UtcNow);
    }
}
