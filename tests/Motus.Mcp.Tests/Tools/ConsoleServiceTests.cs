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

        var entries = service.Read().Entries;
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

        var entries = service.Read().Entries;
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(ConsoleService.PageErrorType, entries[0].Type);
        Assert.AreEqual("[pageerror] Error: kaboom", entries[0].ToString());
    }

    [TestMethod]
    public void Entries_AreNumberedFromOne_AndReadingLeavesThemInPlace()
    {
        var service = new ConsoleService();
        var page = NewPage();
        service.Subscribe(page);

        Assert.AreEqual(1, service.NextSequence, "the first entry is numbered one.");

        page.RaiseConsole("log", "one");
        page.RaiseConsole("log", "two");

        var first = service.Read();
        CollectionAssert.AreEqual(new long[] { 1, 2 }, first.Entries.Select(e => e.Sequence).ToArray());
        Assert.AreEqual(3, first.Next);

        // The read that failed on its way to the agent is simply made again.
        var again = service.Read();
        Assert.AreEqual(2, again.Entries.Count);
        CollectionAssert.AreEqual(
            first.Entries.Select(e => e.Text).ToArray(), again.Entries.Select(e => e.Text).ToArray());
    }

    [TestMethod]
    public void Read_FromTheCursor_ReturnsOnlyWhatArrivedAfterIt()
    {
        var service = new ConsoleService();
        var page = NewPage();
        service.Subscribe(page);
        page.RaiseConsole("log", "before");

        var cursor = service.Read().Next;
        page.RaiseConsole("error", "after");

        var slice = service.Read(cursor);
        Assert.AreEqual(1, slice.Entries.Count);
        Assert.AreEqual("after", slice.Entries[0].Text);
        Assert.AreEqual(0, slice.Dropped);

        // A cursor past the end reads nothing rather than reading everything again.
        Assert.AreEqual(0, service.Read(slice.Next).Entries.Count);
    }

    [TestMethod]
    public void Buffer_IsBounded_EvictingOldest()
    {
        var service = new ConsoleService();
        var page = NewPage();
        service.Subscribe(page);

        for (var i = 0; i < 260; i++)
            page.RaiseConsole("log", $"m{i}");

        var slice = service.Read();
        Assert.AreEqual(250, slice.Entries.Count);
        Assert.AreEqual("m10", slice.Entries[0].Text);
        Assert.AreEqual(11, slice.Entries[0].Sequence, "sequence numbers are not reused when entries are evicted.");
        Assert.AreEqual(261, slice.Next);
    }

    [TestMethod]
    public void Read_WithACursorTheBufferHasPassed_SaysHowMuchWasMissed()
    {
        var service = new ConsoleService();
        var page = NewPage();
        service.Subscribe(page);
        page.RaiseConsole("log", "first");

        var cursor = service.Read().Next;
        for (var i = 0; i < 260; i++)
            page.RaiseConsole("log", $"m{i}");

        var slice = service.Read(cursor);
        Assert.AreEqual(250, slice.Entries.Count);
        Assert.AreEqual("m10", slice.Entries[0].Text);
        Assert.AreEqual(10, slice.Dropped, "ten entries fell out of the buffer before this read reached them.");
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

        Assert.AreEqual(0, service.Read().Entries.Count);
    }
}
