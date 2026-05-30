using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class SessionToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static FakeToolPage Tab(string url, string title = "")
        => new(new AccessibilitySnapshot([], 0, null)) { PageUrl = url, PageTitle = title };

    // --- tabs ---

    [TestMethod]
    public async Task TabList_ShowsIndexUrlAndTitleForEachTab()
    {
        var service = new FakeSessionPageService(Tab("https://a.test", "A"), Tab("https://b.test", "B"));

        var result = await SessionTools.TabListAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        var text = TextOf(result);
        StringAssert.Contains(text, "[0] https://a.test | A");
        StringAssert.Contains(text, "[1] https://b.test | B");
    }

    [TestMethod]
    public async Task TabOpen_NoUrl_OpensBlankTab()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await SessionTools.TabOpenAsync(null, service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(1, service.OpenedTabs);
        StringAssert.Contains(TextOf(result), "about:blank");
    }

    [TestMethod]
    public async Task TabOpen_WithUrl_NavigatesTheNewTab()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await SessionTools.TabOpenAsync("https://new.test", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("https://new.test", service.Tabs[^1].NavigatedUrl);
    }

    [TestMethod]
    public async Task TabSelect_ValidIndex_BringsTabToFront()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"), Tab("https://b.test"));

        var result = await SessionTools.TabSelectAsync(1, service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(1, service.Tabs[1].BringToFrontCount);
    }

    [TestMethod]
    public async Task TabSelect_OutOfRange_ReturnsError()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await SessionTools.TabSelectAsync(5, service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "out of range");
    }

    [TestMethod]
    public async Task TabClose_NoIndex_ClosesTheActiveTab()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"), Tab("https://b.test"));

        var result = await SessionTools.TabCloseAsync(null, service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(service.Tabs[0].CloseCalled);
    }

    [TestMethod]
    public async Task TabClose_Index_ClosesThatTab()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"), Tab("https://b.test"));

        var result = await SessionTools.TabCloseAsync(1, service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(service.Tabs[1].CloseCalled);
        Assert.IsFalse(service.Tabs[0].CloseCalled);
    }

    [TestMethod]
    public async Task TabClose_OutOfRange_ReturnsError()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await SessionTools.TabCloseAsync(9, service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "out of range");
    }

    // --- contexts ---

    [TestMethod]
    public void ContextList_MarksTheActiveContext()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        service.Contexts.Add("userB");

        var result = SessionTools.ContextList(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        var text = TextOf(result);
        StringAssert.Contains(text, "* default");
        StringAssert.Contains(text, "userB");
    }

    [TestMethod]
    public async Task ContextCreate_NewName_Succeeds()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await SessionTools.ContextCreateAsync("userB", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.Contains(service.CreatedContexts, "userB");
    }

    [TestMethod]
    public async Task ContextCreate_DuplicateName_ReturnsError()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        await SessionTools.ContextCreateAsync("userB", service, Ct);

        var result = await SessionTools.ContextCreateAsync("userB", service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "already exists");
    }

    [TestMethod]
    public void ContextSelect_ExistingName_Succeeds()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        service.Contexts.Add("userB");

        var result = SessionTools.ContextSelect("userB", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.Contains(service.SelectedContexts, "userB");
    }

    [TestMethod]
    public void ContextSelect_MissingName_ReturnsError()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = SessionTools.ContextSelect("ghost", service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "No open context");
    }

    [TestMethod]
    public async Task ContextClose_ExistingName_Succeeds()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        await SessionTools.ContextCreateAsync("userB", service, Ct);

        var result = await SessionTools.ContextCloseAsync("userB", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.Contains(service.ClosedContexts, "userB");
    }
}
