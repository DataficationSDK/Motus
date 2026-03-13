using Motus.Analyzers.Analyzers;
using Motus.Analyzers.CodeFixes;

namespace Motus.Analyzers.Tests.CodeFixes;

[TestClass]
public class WrapAwaitUsingCodeFixTests
{
    [TestMethod]
    public async Task AddsAwaitUsingKeywords()
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

        var result = await CodeFixTestHelper.ApplyCodeFixAsync<MissingDisposalAnalyzer, WrapAwaitUsingCodeFix>(source);
        Assert.IsTrue(result.Contains("await") && result.Contains("using"),
            $"Expected 'await using' in result:\n{result}");
    }
}
