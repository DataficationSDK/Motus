using Motus.Analyzers.Analyzers;

namespace Motus.Analyzers.Tests;

[TestClass]
public class MissingDisposalAnalyzerTests
{
    [TestMethod]
    public async Task AwaitUsing_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IBrowser browser)
                {
                    await using var context = await browser.NewContextAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<MissingDisposalAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task BareVar_Diagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IBrowser browser)
                {
                    var context = await browser.NewContextAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<MissingDisposalAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT004", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task NonDisposableType_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            public class Tests
            {
                public void Run()
                {
                    var x = "hello";
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<MissingDisposalAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }
}
