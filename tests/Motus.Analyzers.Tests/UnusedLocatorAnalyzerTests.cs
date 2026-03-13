using Motus.Analyzers.Analyzers;

namespace Motus.Analyzers.Tests;

[TestClass]
public class UnusedLocatorAnalyzerTests
{
    [TestMethod]
    public async Task LocatorActedOn_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IPage page)
                {
                    await page.Locator("button").ClickAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<UnusedLocatorAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task LocatorDiscarded_Diagnostic()
    {
        var source = """
            using Motus.Abstractions;

            public class Tests
            {
                public void Run(IPage page)
                {
                    page.Locator("button");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<UnusedLocatorAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT005", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task LocatorAssigned_NoDiagnostic()
    {
        var source = """
            using Motus.Abstractions;

            public class Tests
            {
                public void Run(IPage page)
                {
                    var btn = page.Locator("button");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<UnusedLocatorAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }
}
