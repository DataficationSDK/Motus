using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Covers the gate in front of the attach tool. Choosing which browser the session drives is the
/// operator's decision rather than the agent's, so the tool refuses until the server was started
/// with the option that allows it.
/// </summary>
[TestClass]
public class BrowserToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [TestMethod]
    public async Task BrowserAttach_ByDefault_IsRefusedAndNamesTheOption()
    {
        var service = new FakeNetworkPageService();

        var result = await BrowserTools.BrowserAttachAsync("http://127.0.0.1:9222", service, Ct);

        Assert.IsTrue(result.IsError);
        var text = TextOf(result);
        StringAssert.Contains(text, "--allow-attach");
        StringAssert.Contains(text, "--connect");
        Assert.IsFalse(service.IsAttached, "nothing should have been connected to");
    }
}
