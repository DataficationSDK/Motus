using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class InteractionToolsUnitTests
{
    private static AccessibilityNode Node(string role, string? name, long? backendId)
        => new(
            NodeId: backendId?.ToString() ?? "x",
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: backendId);

    private static AccessibilitySnapshot Snapshot(params AccessibilityNode[] roots)
        => new(roots, IgnoredCount: 0, DiagnosticMessage: null);

    private static string TextOf(CallToolResult result)
        => ((TextContentBlock)result.Content[0]).Text;

    /// <summary>Builds a service over a one-element page and takes a snapshot so e1 resolves.</summary>
    private static async Task<(FakeToolPage page, FakeActivePageService service)> SnapshottedAsync(
        string role = "button", string? name = "Go")
    {
        var page = new FakeToolPage(Snapshot(Node(role, name, 10)));
        var service = new FakeActivePageService(page);
        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        return (page, service);
    }

    // --- select_option ---

    [TestMethod]
    public async Task SelectOption_RecordsValues()
    {
        var (page, service) = await SnapshottedAsync("combobox", "Country");

        var result = await InteractionTools.SelectOptionAsync(
            "e1", ["us", "ca"], service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.AreEqual(new[] { "us", "ca" }, page.RecordingLocator.SelectedValues?.ToArray());
    }

    // --- hover / clear / focus / scroll_into_view ---

    [TestMethod]
    public async Task Hover_InvokesHover()
    {
        var (page, service) = await SnapshottedAsync();
        await InteractionTools.HoverAsync("e1", service, CancellationToken.None);
        Assert.AreEqual(1, page.RecordingLocator.HoverCount);
    }

    [TestMethod]
    public async Task Clear_InvokesClear()
    {
        var (page, service) = await SnapshottedAsync("textbox", "Name");
        await InteractionTools.ClearAsync("e1", service, CancellationToken.None);
        Assert.AreEqual(1, page.RecordingLocator.ClearCount);
    }

    [TestMethod]
    public async Task Focus_InvokesFocus()
    {
        var (page, service) = await SnapshottedAsync("textbox", "Name");
        await InteractionTools.FocusAsync("e1", service, CancellationToken.None);
        Assert.AreEqual(1, page.RecordingLocator.FocusCount);
    }

    [TestMethod]
    public async Task ScrollIntoView_Invokes()
    {
        var (page, service) = await SnapshottedAsync();
        await InteractionTools.ScrollIntoViewAsync("e1", service, CancellationToken.None);
        Assert.AreEqual(1, page.RecordingLocator.ScrollIntoViewCount);
    }

    // --- press (element) ---

    [TestMethod]
    public async Task Press_RecordsKeyOnElement()
    {
        var (page, service) = await SnapshottedAsync("textbox", "Name");

        await InteractionTools.PressAsync("e1", "Enter", service, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { "Enter" }, page.RecordingLocator.PressedKeys);
    }

    // --- set_checked ---

    [TestMethod]
    public async Task SetChecked_True_Checks()
    {
        var (page, service) = await SnapshottedAsync("checkbox", "Accept");
        await InteractionTools.SetCheckedAsync("e1", true, service, CancellationToken.None);
        Assert.AreEqual(true, page.RecordingLocator.CheckedValue);
    }

    [TestMethod]
    public async Task SetChecked_False_Unchecks()
    {
        var (page, service) = await SnapshottedAsync("checkbox", "Accept");
        await InteractionTools.SetCheckedAsync("e1", false, service, CancellationToken.None);
        Assert.AreEqual(false, page.RecordingLocator.CheckedValue);
    }

    // --- upload_files ---

    [TestMethod]
    public async Task UploadFiles_ReadsFilesAndUploads()
    {
        var (page, service) = await SnapshottedAsync("button", "Upload");
        // A unique name per run: the net8.0 and net10.0 test assemblies run as separate
        // processes and would otherwise contend for one fixed path under the temp dir.
        var path = Path.Combine(Path.GetTempPath(), $"motus_upload_{Guid.NewGuid():N}.txt");
        var bytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            var result = await InteractionTools.UploadFilesAsync(
                "e1", [path], service, CancellationToken.None);

            Assert.IsFalse(result.IsError ?? false);
            var uploaded = page.RecordingLocator.UploadedFiles;
            Assert.IsNotNull(uploaded);
            Assert.AreEqual(1, uploaded.Count);
            Assert.AreEqual(Path.GetFileName(path), uploaded[0].Name);
            Assert.AreEqual("text/plain", uploaded[0].MimeType);
            CollectionAssert.AreEqual(bytes, uploaded[0].Buffer);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public async Task UploadFiles_MissingPath_ReturnsErrorNamingPath()
    {
        var (_, service) = await SnapshottedAsync("button", "Upload");
        var missing = Path.Combine(Path.GetTempPath(), "motus_does_not_exist_12345.bin");

        var result = await InteractionTools.UploadFilesAsync(
            "e1", [missing], service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), missing);
    }

    // --- press_key (page level) ---

    [TestMethod]
    public async Task PressKey_RecordsOnKeyboard_WithoutSnapshot()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await InteractionTools.PressKeyAsync("Escape", service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.AreEqual(new[] { "Escape" }, page.FakeKeyboard.PressedKeys);
    }

    // --- wait_for_element ---

    [TestMethod]
    public async Task WaitForElement_ParsesStateAndWaits()
    {
        var (page, service) = await SnapshottedAsync();

        var result = await InteractionTools.WaitForElementAsync("e1", "hidden", service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(ElementState.Hidden, page.RecordingLocator.WaitedForState);
    }

    [TestMethod]
    public async Task WaitForElement_UnknownState_ReturnsError()
    {
        var (_, service) = await SnapshottedAsync();

        var result = await InteractionTools.WaitForElementAsync("e1", "sideways", service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "sideways");
    }

    // --- wait_for (page level) ---

    [TestMethod]
    public async Task WaitFor_Time_CallsTimeout()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        await InteractionTools.WaitForAsync(250, null, null, service, CancellationToken.None);

        Assert.AreEqual(250d, page.WaitedTimeoutMs);
    }

    [TestMethod]
    public async Task WaitFor_Text_CallsFunctionWithTextArg()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        await InteractionTools.WaitForAsync(null, "Welcome", null, service, CancellationToken.None);

        Assert.AreEqual(1, page.WaitedFunctions.Count);
        Assert.AreEqual("Welcome", page.WaitedFunctionArgs[0]);
    }

    [TestMethod]
    public async Task WaitFor_NoParams_ReturnsError()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await InteractionTools.WaitForAsync(null, null, null, service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
    }

    // --- shared ref guidance ---

    [TestMethod]
    public async Task RefTool_NoSnapshot_ReturnsGuidance()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var result = await InteractionTools.HoverAsync("e1", service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "snapshot");
    }

    [TestMethod]
    public async Task RefTool_StaleRef_ReturnsGuidance()
    {
        var (_, service) = await SnapshottedAsync();

        var result = await InteractionTools.HoverAsync("e999", service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "e999");
    }
}
