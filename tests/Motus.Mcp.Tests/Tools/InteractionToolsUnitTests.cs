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
        string role = "button", string? name = "Go", double? actionTimeout = null)
    {
        var page = new FakeToolPage(Snapshot(Node(role, name, 10)));
        var service = new FakeActivePageService(
            page, options: new McpServerLaunchOptions { ActionTimeout = actionTimeout });
        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
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

    /// <summary>
    /// The configured action timeout reaches the call, so a session that was started with
    /// <c>--timeout</c> does not silently wait out the framework default here.
    /// </summary>
    [TestMethod]
    public async Task SelectOption_CarriesTheConfiguredTimeout()
    {
        var (page, service) = await SnapshottedAsync("combobox", "Country", actionTimeout: 2_500);

        await InteractionTools.SelectOptionAsync("e1", ["us"], service, CancellationToken.None);

        Assert.AreEqual(2_500d, page.RecordingLocator.SelectOptionTimeout);
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

    [TestMethod]
    public async Task Press_CarriesTheConfiguredTimeout()
    {
        var (page, service) = await SnapshottedAsync("textbox", "Name", actionTimeout: 2_500);

        await InteractionTools.PressAsync("e1", "Enter", service, CancellationToken.None);

        Assert.AreEqual(2_500d, page.RecordingLocator.PressTimeout);
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

    /// <summary>
    /// A directory the reads are allowed to come from, standing in for the roots an MCP client
    /// reports. A unique name per run: the net8.0 and net10.0 test assemblies run as separate
    /// processes and would otherwise contend for one fixed path under the temp dir.
    /// </summary>
    private static (string Directory, SecurityPolicy Policy) ReadableDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"motus_upload_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var policy = new SecurityPolicy(new McpServerLaunchOptions { OutputDirectory = directory })
        {
            ReadRootsOverride = _ => new ValueTask<IReadOnlyList<string>>(new[] { directory }),
        };

        return (directory, policy);
    }

    [TestMethod]
    public async Task UploadFiles_ReadsFilesAndUploads()
    {
        var (page, service) = await SnapshottedAsync("button", "Upload");
        var (directory, policy) = ReadableDirectory();
        var path = Path.Combine(directory, "upload.txt");
        var bytes = new byte[] { 1, 2, 3, 4 };
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            var result = await InteractionTools.UploadFilesAsync(
                "e1", [path], service, CancellationToken.None, server: null, policy: policy);

            Assert.IsFalse(result.IsError ?? false, TextOf(result));
            var uploaded = page.RecordingLocator.UploadedFiles;
            Assert.IsNotNull(uploaded);
            Assert.AreEqual(1, uploaded.Count);
            Assert.AreEqual(Path.GetFileName(path), uploaded[0].Name);
            Assert.AreEqual("text/plain", uploaded[0].MimeType);
            CollectionAssert.AreEqual(bytes, uploaded[0].Buffer);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// The roots are asked for once per call rather than once per file, so an upload of several
    /// files is one round trip to the client instead of several.
    /// </summary>
    [TestMethod]
    public async Task UploadFiles_WithSeveralPaths_AsksTheClientWhereItIsWorkingOnce()
    {
        var (_, service) = await SnapshottedAsync("button", "Upload");
        var directory = Path.Combine(Path.GetTempPath(), $"motus_upload_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var asked = 0;
        var policy = new SecurityPolicy(new McpServerLaunchOptions { OutputDirectory = directory })
        {
            ReadRootsOverride = _ =>
            {
                asked++;
                return new ValueTask<IReadOnlyList<string>>(new[] { directory });
            },
        };

        var paths = new[] { "one.txt", "two.txt", "three.txt" }
            .Select(name => Path.Combine(directory, name))
            .ToArray();
        foreach (var path in paths)
            await File.WriteAllTextAsync(path, "content");

        try
        {
            var result = await InteractionTools.UploadFilesAsync(
                "e1", paths, service, CancellationToken.None, server: null, policy: policy);

            Assert.IsFalse(result.IsError ?? false, TextOf(result));
            Assert.AreEqual(1, asked);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task UploadFiles_MissingPath_ReturnsErrorNamingPath()
    {
        var (_, service) = await SnapshottedAsync("button", "Upload");
        var (directory, policy) = ReadableDirectory();
        var missing = Path.Combine(directory, "motus_does_not_exist_12345.bin");
        try
        {
            var result = await InteractionTools.UploadFilesAsync(
                "e1", [missing], service, CancellationToken.None, server: null, policy: policy);

            Assert.IsTrue(result.IsError);
            StringAssert.Contains(TextOf(result), missing);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task UploadFiles_PathOutsideTheReadableDirectories_IsRefusedWithoutReadingIt()
    {
        var (page, service) = await SnapshottedAsync("button", "Upload");
        var (directory, policy) = ReadableDirectory();
        var outside = Path.Combine(Path.GetTempPath(), $"motus_outside_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "secret");
        try
        {
            var result = await InteractionTools.UploadFilesAsync(
                "e1", [outside], service, CancellationToken.None, server: null, policy: policy);

            Assert.IsTrue(result.IsError);
            StringAssert.Contains(TextOf(result), "Reads are confined to");
            Assert.IsNull(page.RecordingLocator.UploadedFiles, "nothing should have reached the page");
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public async Task UploadFiles_WithUnrestrictedFileAccess_ReadsAnywhere()
    {
        var (page, service) = await SnapshottedAsync("button", "Upload");
        var (directory, _) = ReadableDirectory();
        var unrestricted = new SecurityPolicy(new McpServerLaunchOptions
        {
            OutputDirectory = directory,
            AllowUnrestrictedFileAccess = true,
        });
        var outside = Path.Combine(Path.GetTempPath(), $"motus_outside_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(outside, "secret");
        try
        {
            var result = await InteractionTools.UploadFilesAsync(
                "e1", [outside], service, CancellationToken.None, server: null, policy: unrestricted);

            Assert.IsFalse(result.IsError ?? false, TextOf(result));
            Assert.AreEqual(1, page.RecordingLocator.UploadedFiles?.Count);
        }
        finally
        {
            File.Delete(outside);
            Directory.Delete(directory, recursive: true);
        }
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

        await InteractionTools.WaitForAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            time: 250,
            text: null,
            text_gone: null);

        Assert.AreEqual(250d, page.WaitedTimeoutMs);
    }

    [TestMethod]
    public async Task WaitFor_Text_CallsFunctionWithTextArg()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        await InteractionTools.WaitForAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            time: null,
            text: "Welcome",
            text_gone: null);

        Assert.AreEqual(1, page.WaitedFunctions.Count);
        Assert.AreEqual("Welcome", page.WaitedFunctionArgs[0]);
    }

    [TestMethod]
    public async Task WaitFor_NoParams_ReturnsError()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await InteractionTools.WaitForAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            time: null,
            text: null,
            text_gone: null);

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

    [TestMethod]
    public async Task RefTool_WithASelector_ActsWithNoSnapshotTaken()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var result = await InteractionTools.HoverAsync("#submit", service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.AreEqual(1, page.RecordingLocator.HoverCount);
        Assert.AreEqual("#submit", page.ResolvedSelector);
    }

    [TestMethod]
    public async Task EveryElementTool_TakesASelector()
    {
        // One tool proving the shared path is not enough: each of these has its own signature, and
        // a tool added later that resolves its own target would not show up in the test above.
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);
        var ct = CancellationToken.None;

        var results = new[]
        {
            await InteractionTools.HoverAsync("#a", service, ct),
            await InteractionTools.FocusAsync("#b", service, ct),
            await InteractionTools.ClearAsync("#c", service, ct),
            await InteractionTools.ScrollIntoViewAsync("#d", service, ct),
            await InteractionTools.SetCheckedAsync("#e", true, service, ct),
            await InteractionTools.PressAsync("#f", "Enter", service, ct),
            await InteractionTools.SelectOptionAsync("#g", ["pro"], service, ct),
            await InteractionTools.WaitForElementAsync("#h", "visible", service, ct),
        };

        foreach (var result in results)
            Assert.IsFalse(result.IsError ?? false, TextOf(result));

        Assert.AreEqual("#h", page.ResolvedSelector);
    }
}
