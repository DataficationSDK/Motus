using Motus.Analyzers.Analyzers;

namespace Motus.Analyzers.Tests;

[TestClass]
public class HardcodedDelayAnalyzerTests
{
    [TestMethod]
    public async Task TaskDelay_Diagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            public class Tests
            {
                public async Task Run()
                {
                    await Task.Delay(1000);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<HardcodedDelayAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT002", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task ThreadSleep_Diagnostic()
    {
        var source = """
            using System.Threading;

            public class Tests
            {
                public void Run()
                {
                    Thread.Sleep(1000);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<HardcodedDelayAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT002", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task WaitForLoadState_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IPage page)
                {
                    await page.WaitForLoadStateAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<HardcodedDelayAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }
}
