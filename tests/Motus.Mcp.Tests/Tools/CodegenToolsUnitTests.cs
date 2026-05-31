using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class CodegenToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [TestMethod]
    public async Task GeneratePom_WhenAnalysisCannotRun_ReturnsErrorRatherThanThrows()
    {
        // The analysis engine drives a real browser page; a fake page cannot satisfy it.
        // The tool must surface that as an error result, never an unhandled exception
        // (which the protocol would turn into an opaque failure). The happy path is
        // covered by the browser integration test.
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        var service = new FakeActivePageService(page);

        var result = await CodegenTools.GeneratePomAsync(@namespace: null, class_name: null, service, Ct);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "page object model");
    }
}
