using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class DialogServiceTests
{
    private static FakeToolPage Page() => new(new AccessibilitySnapshot([], 0, null));

    [TestMethod]
    public void TakePendingDialog_WhenNone_ReturnsNull()
    {
        var service = new DialogService();
        Assert.IsNull(service.TakePendingDialog());
    }

    [TestMethod]
    public void Subscribe_ThenDialogFires_CapturesIt()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);

        var dialog = new FakeDialog(DialogType.Alert, "hi");
        page.RaiseDialog(dialog);

        Assert.AreSame(dialog, service.TakePendingDialog());
    }

    [TestMethod]
    public void TakePendingDialog_ClearsAfterReturning()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);
        page.RaiseDialog(new FakeDialog());

        Assert.IsNotNull(service.TakePendingDialog());
        Assert.IsNull(service.TakePendingDialog());
    }

    [TestMethod]
    public void SecondDialog_OverwritesTheFirst()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);

        page.RaiseDialog(new FakeDialog(DialogType.Alert, "first"));
        var second = new FakeDialog(DialogType.Confirm, "second");
        page.RaiseDialog(second);

        Assert.AreSame(second, service.TakePendingDialog());
    }

    [TestMethod]
    public void Subscribe_DifferentPage_StopsCapturingFromThePrevious()
    {
        var service = new DialogService();
        var first = Page();
        var second = Page();

        service.Subscribe(first);
        service.Subscribe(second);

        first.RaiseDialog(new FakeDialog(DialogType.Alert, "stale"));
        Assert.IsNull(service.TakePendingDialog(), "the previous page should have been unsubscribed");

        var live = new FakeDialog(DialogType.Alert, "live");
        second.RaiseDialog(live);
        Assert.AreSame(live, service.TakePendingDialog());
    }

    [TestMethod]
    public void Subscribe_SamePageTwice_DoesNotDoubleCapture()
    {
        var service = new DialogService();
        var page = Page();

        service.Subscribe(page);
        service.Subscribe(page);

        // A single fire should leave a single pending dialog (no duplicate handlers).
        page.RaiseDialog(new FakeDialog());
        Assert.IsNotNull(service.TakePendingDialog());
        Assert.IsNull(service.TakePendingDialog());
    }
}
