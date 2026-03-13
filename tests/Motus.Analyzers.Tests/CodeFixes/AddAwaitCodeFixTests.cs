using Motus.Analyzers.Analyzers;
using Motus.Analyzers.CodeFixes;

namespace Motus.Analyzers.Tests.CodeFixes;

[TestClass]
public class AddAwaitCodeFixTests
{
    [TestMethod]
    public async Task AddsAwaitToUnawaitedCall()
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

        var result = await CodeFixTestHelper.ApplyCodeFixAsync<NonAwaitedCallAnalyzer, AddAwaitCodeFix>(source);
        Assert.IsTrue(result.Contains("await page.GotoAsync"), $"Expected 'await' keyword in result:\n{result}");
    }

    [TestMethod]
    public async Task AddsAsyncModifierToMethod()
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

        var result = await CodeFixTestHelper.ApplyCodeFixAsync<NonAwaitedCallAnalyzer, AddAwaitCodeFix>(source);
        Assert.IsTrue(result.Contains("async"), $"Expected 'async' modifier in result:\n{result}");
    }
}
