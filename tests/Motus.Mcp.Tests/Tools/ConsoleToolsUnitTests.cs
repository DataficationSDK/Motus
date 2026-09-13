using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class ConsoleToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [TestMethod]
    public void ConsoleMessages_Empty_ReportsNone()
    {
        var result = ConsoleTools.ConsoleMessages(new ConsoleService(), Ct);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "No console messages");
    }

    [TestMethod]
    public void ConsoleMessages_RendersEntries_AndRepeatsOnASecondRead()
    {
        var console = new ConsoleService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        console.Subscribe(page);
        page.RaiseConsole("error", "boom");
        page.RaisePageError("Error: kaboom");

        var result = ConsoleTools.ConsoleMessages(console, Ct);

        var text = TextOf(result);
        StringAssert.Contains(text, "[error] boom");
        StringAssert.Contains(text, "[pageerror] Error: kaboom");
        StringAssert.Contains(text, "next=3");
        // Reading no longer empties the buffer, so a retry sees the same entries.
        StringAssert.Contains(TextOf(ConsoleTools.ConsoleMessages(console, Ct)), "[error] boom");
    }

    [TestMethod]
    public void ConsoleMessages_WithSince_ReturnsOnlyWhatFollowedTheCursor()
    {
        var console = new ConsoleService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        console.Subscribe(page);
        page.RaiseConsole("log", "before");
        page.RaiseConsole("error", "after");

        var text = TextOf(ConsoleTools.ConsoleMessages(console, Ct, since: 2));

        StringAssert.Contains(text, "[error] after");
        Assert.IsFalse(text.Contains("before", StringComparison.Ordinal), text);
        StringAssert.Contains(
            TextOf(ConsoleTools.ConsoleMessages(console, Ct, since: 3)), "No console messages have been logged since 3.");
    }
}
