using Motus.Analyzers.Analyzers;

namespace Motus.Analyzers.Tests;

[TestClass]
public class DeprecatedSelectorAnalyzerTests
{
    [TestMethod]
    public async Task CssPrefix_Diagnostic()
    {
        var source = """
            using Motus.Abstractions;

            public class Tests
            {
                public void Run(IPage page)
                {
                    page.Locator("css=button.submit");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<DeprecatedSelectorAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT006", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task TextPrefix_Diagnostic()
    {
        var source = """
            using Motus.Abstractions;

            public class Tests
            {
                public void Run(IPage page)
                {
                    page.Locator("text=Submit");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<DeprecatedSelectorAnalyzer>(source);
        Assert.AreEqual(1, diagnostics.Length);
        Assert.AreEqual("MOT006", diagnostics[0].Id);
    }

    [TestMethod]
    public async Task CleanSelector_NoDiagnostic()
    {
        var source = """
            using Motus.Abstractions;

            public class Tests
            {
                public void Run(IPage page)
                {
                    page.Locator("button.submit");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync<DeprecatedSelectorAnalyzer>(source);
        Assert.AreEqual(0, diagnostics.Length);
    }
}
