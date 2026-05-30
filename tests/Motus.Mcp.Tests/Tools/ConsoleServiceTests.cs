using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class ConsoleServiceTests
{
    private static FakeToolPage NewPage() => new(new AccessibilitySnapshot([], 0, null));

    [TestMethod]
    public void Console_IsCaptured_WithTypeAndText()
    {
        var service = new ConsoleService();
        var page = NewPage();
        service.Subscribe(page);

        page.RaiseConsole("error", "boom");

        var entries = service.Drain();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("error", entries[0].Type);
        Assert.AreEqual("[error] boom", entries[0].ToString());
    }

    [TestMethod]
    public void PageError_IsCaptured_AsPageErrorType()
    {
        var service = new ConsoleService();
        var page = NewPage();
        service.Subscribe(page);

        page.RaisePageError("Error: kaboom");

        var entries = service.Drain();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(ConsoleService.PageErrorType, entries[0].Type);
        Assert.AreEqual("[pageerror] Error: kaboom", entries[0].ToString());
    }

    [TestMethod]
    public void Drain_ClearsTheBuffer()
    {
        var service = new ConsoleService();
        var page = NewPage();
        service.Subscribe(page);
        page.RaiseConsole("log", "one");

        Assert.AreEqual(1, service.Drain().Count);
        Assert.AreEqual(0, service.Drain().Count);
    }

    [TestMethod]
    public void Buffer_IsBounded_EvictingOldest()
    {
        var service = new ConsoleService();
        var page = NewPage();
        service.Subscribe(page);

        for (var i = 0; i < 260; i++)
            page.RaiseConsole("log", $"m{i}");

        var entries = service.Drain();
        Assert.AreEqual(250, entries.Count);
        Assert.AreEqual("m10", entries[0].Text);
    }

    [TestMethod]
    public void SubscribingNewPage_DetachesThePrevious()
    {
        var service = new ConsoleService();
        var first = NewPage();
        var second = NewPage();

        service.Subscribe(first);
        service.Subscribe(second);
        first.RaiseConsole("log", "stale");

        Assert.AreEqual(0, service.Drain().Count);
    }
}
