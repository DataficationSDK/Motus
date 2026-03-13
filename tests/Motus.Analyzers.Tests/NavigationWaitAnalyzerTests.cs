using Motus.Analyzers.Analyzers;

namespace Motus.Analyzers.Tests;

[TestClass]
public class NavigationWaitAnalyzerTests
{
    [TestMethod]
    public async Task NavThenWaitThenAssert_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IPage page)
                {
                    await page.GotoAsync("https://example.com");
                    await page.WaitForLoadStateAsync();
                    await page.Locator("h1").ClickAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NavigationWaitAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task NavThenAssertWithoutWait_Diagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IPage page)
                {
                    await page.GotoAsync("https://example.com");
                    await page.Locator("h1").ClickAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NavigationWaitAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT007", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task NonNavThenAssert_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IPage page)
                {
                    await page.Locator("button").ClickAsync();
                    await page.Locator("h1").ClickAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NavigationWaitAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task NavAsLastStatement_Diagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IPage page)
                {
                    await page.GotoAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NavigationWaitAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT007", diagnostics[0].Id);
    }
}
