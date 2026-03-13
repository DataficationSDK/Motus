using Motus.Analyzers.Analyzers;
using Motus.Analyzers.CodeFixes;

namespace Motus.Analyzers.Tests.CodeFixes;

[TestClass]
public class ReplaceDelayCodeFixTests
{
    [TestMethod]
    public async Task ReplacesTaskDelayWithWaitForLoadState()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            public class Tests
            {
                public async Task Run(IPage page)
                {
                    await Task.Delay(1000);
                }
            }
            """;

        var result = await CodeFixTestHelper.ApplyCodeFixAsync<HardcodedDelayAnalyzer, ReplaceDelayCodeFix>(source);
        Assert.IsTrue(result.Contains("WaitForLoadStateAsync"),
            $"Expected 'WaitForLoadStateAsync' in result:\n{result}");
    }
}
