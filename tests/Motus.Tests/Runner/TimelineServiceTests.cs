using Motus.Runner.Services.Timeline;

namespace Motus.Tests.Runner;

[TestClass]
public class TimelineServiceTests
{
    [TestMethod]
    public void AddEntry_FiresChanged()
    {
        var svc = new TimelineService();
        var fired = false;
        svc.TimelineChanged += () => fired = true;

        var entry = MakeEntry(0);
        svc.AddEntry(entry);

        Assert.IsTrue(fired);
        Assert.AreEqual(1, svc.Entries.Count);
    }

    [TestMethod]
    public void SelectEntry_UpdatesIndex()
    {
        var svc = new TimelineService();
        svc.AddEntry(MakeEntry(0));
        svc.AddEntry(MakeEntry(1));

        svc.SelectEntry(1);

        Assert.AreEqual(1, svc.SelectedIndex);
        Assert.IsNotNull(svc.SelectedEntry);
        Assert.AreEqual(1, svc.SelectedEntry!.Index);
    }

    [TestMethod]
    public void Clear_RemovesAll()
    {
        var svc = new TimelineService();
        svc.AddEntry(MakeEntry(0));
        svc.AddEntry(MakeEntry(1));
        svc.SelectEntry(0);

        svc.Clear();

        Assert.AreEqual(0, svc.Entries.Count);
        Assert.IsNull(svc.SelectedIndex);
        Assert.IsNull(svc.SelectedEntry);
    }

    [TestMethod]
    public void SelectEntry_OutOfRange_DoesNotUpdate()
    {
        var svc = new TimelineService();
        svc.AddEntry(MakeEntry(0));
        svc.SelectEntry(0);

        svc.SelectEntry(99);

        Assert.AreEqual(0, svc.SelectedIndex);
    }

    [TestMethod]
    public void ClearSelection_NullsIndex()
    {
        var svc = new TimelineService();
        svc.AddEntry(MakeEntry(0));
        svc.SelectEntry(0);

        svc.ClearSelection();

        Assert.IsNull(svc.SelectedIndex);
    }

    private static TimelineEntry MakeEntry(int index) => new(
        Index: index,
        Timestamp: DateTime.UtcNow,
        ActionType: "click",
        Selector: "#btn",
        Duration: TimeSpan.FromMilliseconds(50),
        ScreenshotBefore: null,
        ScreenshotAfter: null,
        HasError: false,
        ErrorMessage: null,
        NetworkRequests: [],
        ConsoleMessages: []);
}
