using Motus.Analyzers.Analyzers;

namespace Motus.Analyzers.Tests;

[TestClass]
public class NonAwaitedCallAnalyzerTests
{
    [TestMethod]
    public async Task AwaitedCall_NoDiagnostic()
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

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NonAwaitedCallAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task UnawaitedPageCall_Diagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public void Run(IPage page)
                {
                    page.GotoAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NonAwaitedCallAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT001", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task UnawaitedLocatorCall_Diagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public void Run(ILocator locator)
                {
                    locator.ClickAsync();
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NonAwaitedCallAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT001", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task UnawaitedNonMotusCall_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;

            public class Tests
            {
                public void Run()
                {
                    Task.Delay(100);
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NonAwaitedCallAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }

    [TestMethod]
    public async Task AssignedToVariable_NoDiagnostic()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public void Run(IPage page)
                {
                    var task = page.GotoAsync("https://example.com");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<NonAwaitedCallAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }
}
