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

        var result = await SessionTools.TabOpenAsync(
            pageService: service,
            cancellationToken: Ct,
            url: null);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(1, service.OpenedTabs);
        StringAssert.Contains(TextOf(result), "about:blank");
    }

    [TestMethod]
    public async Task TabOpen_WithUrl_NavigatesTheNewTab()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await SessionTools.TabOpenAsync(
            pageService: service,
            cancellationToken: Ct,
            url: "https://new.test");

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

        var result = await SessionTools.TabCloseAsync(
            pageService: service,
            cancellationToken: Ct,
            index: null);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(service.Tabs[0].CloseCalled);
    }

    [TestMethod]
    public async Task TabClose_Index_ClosesThatTab()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"), Tab("https://b.test"));

        var result = await SessionTools.TabCloseAsync(
            pageService: service,
            cancellationToken: Ct,
            index: 1);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(service.Tabs[1].CloseCalled);
        Assert.IsFalse(service.Tabs[0].CloseCalled);
    }

    /// <summary>
    /// One index runs across every context the session holds, context by context, so a tab is
    /// addressable wherever it is rather than only while its context is the active one.
    /// </summary>
    [TestMethod]
    public async Task TabList_NumbersEveryContextsTabsInOneSequence()
    {
        var service = new FakeSessionPageService(Tab("https://a.test", "A"), Tab("https://b.test", "B"));
        service.AddTabIn("work", "https://c.test");

        var result = await SessionTools.TabListAsync(service, Ct);

        var text = TextOf(result);
        StringAssert.Contains(text, "[0] https://a.test | A | context: default");
        StringAssert.Contains(text, "[1] https://b.test | B | context: default");
        StringAssert.Contains(text, "[2] https://c.test | context: work");
    }

    /// <summary>
    /// With one context there is no choice to make, so naming it on every row would be a column of
    /// the same word.
    /// </summary>
    [TestMethod]
    public async Task TabList_WithOneContext_DoesNotNameIt()
    {
        var service = new FakeSessionPageService(Tab("https://a.test", "A"));

        var text = TextOf(await SessionTools.TabListAsync(service, Ct));

        Assert.IsFalse(text.Contains("context:", StringComparison.Ordinal), text);
    }

    /// <summary>
    /// Naming a tab says where the session should be working. Bringing the tab to the front and
    /// leaving the session in the context it came from would make every call that followed act on a
    /// page somewhere else.
    /// </summary>
    [TestMethod]
    public async Task TabSelect_ATabInAnotherContext_SwitchesToThatContext()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        var elsewhere = service.AddTabIn("work", "https://c.test");

        var result = await SessionTools.TabSelectAsync(1, service, Ct);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.AreEqual(1, elsewhere.BringToFrontCount);
        Assert.AreEqual("work", service.GetActiveContextName());
        CollectionAssert.Contains(service.SelectedContexts, "work");
    }

    [TestMethod]
    public async Task TabSelect_ATabInTheActiveContext_LeavesTheContextAlone()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"), Tab("https://b.test"));

        await SessionTools.TabSelectAsync(1, service, Ct);

        Assert.AreEqual(0, service.SelectedContexts.Count,
            "a tab in the active context is no reason to switch context.");
    }

    [TestMethod]
    public async Task TabClose_ATabInAnotherContext_ClosesIt()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        var elsewhere = service.AddTabIn("work", "https://c.test");

        var result = await SessionTools.TabCloseAsync(
            pageService: service,
            cancellationToken: Ct,
            index: 1);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.IsTrue(elsewhere.CloseCalled);
    }

    [TestMethod]
    public async Task TabClose_OutOfRange_ReturnsError()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await SessionTools.TabCloseAsync(
            pageService: service,
            cancellationToken: Ct,
            index: 9);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "out of range");
    }

    // --- contexts ---

    [TestMethod]
    public void ContextList_MarksTheActiveContext()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        service.Contexts.Add("userB");

        var result = ContextTools.ContextList(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        var text = TextOf(result);
        StringAssert.Contains(text, "* default");
        StringAssert.Contains(text, "userB");
    }

    [TestMethod]
    public async Task ContextCreate_NewName_Succeeds()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await ContextTools.ContextCreateAsync("userB", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.Contains(service.CreatedContexts, "userB");
    }

    [TestMethod]
    public async Task ContextCreate_DuplicateName_ReturnsError()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        await ContextTools.ContextCreateAsync("userB", service, Ct);

        var result = await ContextTools.ContextCreateAsync("userB", service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "already exists");
    }

    [TestMethod]
    public void ContextSelect_ExistingName_Succeeds()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        service.Contexts.Add("userB");

        var result = ContextTools.ContextSelect("userB", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.Contains(service.SelectedContexts, "userB");
    }

    [TestMethod]
    public void ContextSelect_MissingName_ReturnsError()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = ContextTools.ContextSelect("ghost", service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "No open context");
    }

    [TestMethod]
    public async Task ContextClose_ExistingName_Succeeds()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));
        await ContextTools.ContextCreateAsync("userB", service, Ct);

        var result = await ContextTools.ContextCloseAsync("userB", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.Contains(service.ClosedContexts, "userB");
    }

    // --- tab_open: the local filesystem ---

    [TestMethod]
    public async Task TabOpen_AtAFileUrl_IsRefusedWithoutOpeningATab()
    {
        var service = new FakeSessionPageService(Tab("https://a.test"));

        var result = await SessionTools.TabOpenAsync(
            pageService: service,
            cancellationToken: Ct,
            url: "file:///etc/hosts");

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "file:// navigation is disabled");
        Assert.AreEqual(0, service.OpenedTabs);
    }
}
