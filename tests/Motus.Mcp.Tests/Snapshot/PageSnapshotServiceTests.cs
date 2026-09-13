using Motus.Abstractions;
using Motus.Mcp;
using Motus.Mcp.Tests.Tools;

namespace Motus.Mcp.Tests.Snapshot;

[TestClass]
public class PageSnapshotServiceTests
{
    [TestMethod]
    public void ResolveRef_BeforeSnapshot_Throws()
    {
        var service = new PageSnapshotService(new FakeAccessibilityPage(EmptySnapshot()));

        Assert.ThrowsException<SnapshotNotTakenException>(() => service.ResolveRef("e1"));
    }

    [TestMethod]
    public async Task ResolveRef_UnknownRef_ThrowsStale()
    {
        var snapshot = new AccessibilitySnapshot(
            Roots:
            [
                new AccessibilityNode("1", "button", "Go", null, null,
                    new Dictionary<string, string?>(), [], BackendDOMNodeId: 5),
            ],
            IgnoredCount: 0,
            DiagnosticMessage: null);

        var service = new PageSnapshotService(new FakeAccessibilityPage(snapshot));
        await service.TakeSnapshotAsync();

        var ex = Assert.ThrowsException<StaleRefException>(() => service.ResolveRef("e999"));
        Assert.AreEqual("e999", ex.RefId);
    }

    [TestMethod]
    public void ResolveRef_WithASelector_NeedsNoSnapshot()
    {
        var page = new FakeAccessibilityPage(EmptySnapshot());
        var service = new PageSnapshotService(page);

        var locator = service.ResolveRef("#late-btn");

        Assert.IsNotNull(locator, "a selector describes the element itself, so nothing has to be read first.");
        Assert.AreEqual("#late-btn", page.ResolvedSelector);
    }

    [TestMethod]
    public async Task ResolveRef_WithARefTheSnapshotDoesNotHold_IsStaleRatherThanASelector()
    {
        var snapshot = new AccessibilitySnapshot(
            Roots:
            [
                new AccessibilityNode("1", "button", "Go", null, null,
                    new Dictionary<string, string?>(), [], BackendDOMNodeId: 5),
            ],
            IgnoredCount: 0,
            DiagnosticMessage: null);

        var page = new FakeAccessibilityPage(snapshot);
        var service = new PageSnapshotService(page);
        await service.TakeSnapshotAsync();

        // Running a ref as a selector would answer "no element matched e99", which sends the agent
        // looking at the page rather than at the snapshot it took too long ago.
        Assert.ThrowsException<StaleRefException>(() => service.ResolveRef("e99"));
        Assert.IsNull(page.ResolvedSelector);
    }

    [TestMethod]
    public async Task ResolveRef_WithAFrameScopedRefShape_IsReadAsARef()
    {
        var page = new FakeAccessibilityPage(EmptySnapshot());
        var service = new PageSnapshotService(page);
        await service.TakeSnapshotAsync();

        // Refs that name the frame they came from are the next step for the snapshot, and a
        // selector is never shaped like one, so they are claimed here before they are assigned.
        Assert.ThrowsException<StaleRefException>(() => service.ResolveRef("f1e2"));
        Assert.IsNull(page.ResolvedSelector);
    }

    [TestMethod]
    public async Task TakeSnapshot_StoresLastSnapshotText()
    {
        var snapshot = new AccessibilitySnapshot(
            Roots:
            [
                new AccessibilityNode("1", "button", "Go", null, null,
                    new Dictionary<string, string?>(), [], BackendDOMNodeId: 5),
            ],
            IgnoredCount: 0,
            DiagnosticMessage: null);

        var service = new PageSnapshotService(new FakeAccessibilityPage(snapshot));

        var text = await service.TakeSnapshotAsync();

        Assert.AreEqual(text, service.LastSnapshot);
        StringAssert.Contains(text, "- button \"Go\" [ref=e1]");
    }

    [TestMethod]
    public void GetRefForNodeId_BeforeSnapshot_ReturnsNull()
    {
        var service = new PageSnapshotService(new FakeAccessibilityPage(EmptySnapshot()));

        Assert.IsNull(service.GetRefForNodeId(5));
    }

    [TestMethod]
    public async Task GetRefForNodeId_AfterSnapshot_ReturnsRefForKnownNode_AndNullOtherwise()
    {
        var snapshot = new AccessibilitySnapshot(
            Roots:
            [
                new AccessibilityNode("1", "img", "Logo", null, null,
                    new Dictionary<string, string?>(), [], BackendDOMNodeId: 5),
                new AccessibilityNode("2", "button", "Go", null, null,
                    new Dictionary<string, string?>(), [], BackendDOMNodeId: 7),
            ],
            IgnoredCount: 0,
            DiagnosticMessage: null);

        var service = new PageSnapshotService(new FakeAccessibilityPage(snapshot));
        await service.TakeSnapshotAsync();

        // Refs are assigned in document order, so the inverse maps each id back to its ref.
        Assert.AreEqual("e1", service.GetRefForNodeId(5));
        Assert.AreEqual("e2", service.GetRefForNodeId(7));
        Assert.IsNull(service.GetRefForNodeId(999));
    }

    [TestMethod]
    public async Task GetRefForNodeId_ForANodeTheSnapshotDidNotAddress_ReturnsNull_AndItsTextInstead()
    {
        // An unnamed image is in the tree but not worth a ref; a paragraph is not worth one either
        // but carries text that says which one it is.
        var snapshot = new AccessibilitySnapshot(
            Roots:
            [
                new AccessibilityNode("1", "img", "", null, null,
                    new Dictionary<string, string?>(), [], BackendDOMNodeId: 5),
                new AccessibilityNode("2", "paragraph", "", null, null,
                    new Dictionary<string, string?>(),
                    [
                        new AccessibilityNode("3", "StaticText", "Terms apply.", null, null,
                            new Dictionary<string, string?>(), [], BackendDOMNodeId: 6),
                    ],
                    BackendDOMNodeId: 7),
                new AccessibilityNode("4", "button", "Go", null, null,
                    new Dictionary<string, string?>(), [], BackendDOMNodeId: 8),
            ],
            IgnoredCount: 0,
            DiagnosticMessage: null);

        var service = new PageSnapshotService(new FakeAccessibilityPage(snapshot));
        Assert.IsNull(service.GetTextForNodeId(7), "no snapshot has been taken yet");

        await service.TakeSnapshotAsync();

        Assert.IsNull(service.GetRefForNodeId(5));
        Assert.IsNull(service.GetTextForNodeId(5));
        Assert.IsNull(service.GetRefForNodeId(7));
        Assert.AreEqual("Terms apply.", service.GetTextForNodeId(7));
        Assert.AreEqual("e1", service.GetRefForNodeId(8));
    }

    [TestMethod]
    public async Task TakeSnapshot_WithNoAddressableNodes_AppendsCoordinateWorkflowNote()
    {
        var service = new PageSnapshotService(new FakeAccessibilityPage(EmptySnapshot()));

        var text = await service.TakeSnapshotAsync();

        StringAssert.Contains(text, "no addressable elements were found");
        StringAssert.Contains(text, "click_xy");
        Assert.AreEqual(text, service.LastSnapshot);
    }

    [TestMethod]
    public async Task TakeSnapshot_WithAddressableNodes_HasNoDegradedNote()
    {
        var snapshot = new AccessibilitySnapshot(
            Roots:
            [
                new AccessibilityNode("1", "button", "Go", null, null,
                    new Dictionary<string, string?>(), [], BackendDOMNodeId: 5),
            ],
            IgnoredCount: 0,
            DiagnosticMessage: null);

        var service = new PageSnapshotService(new FakeAccessibilityPage(snapshot));

        var text = await service.TakeSnapshotAsync();

        Assert.IsFalse(text.Contains("no addressable elements"),
            "a snapshot with refs should not carry the degraded note");
    }

    [TestMethod]
    public async Task TakeSnapshot_WhenTheBrowserCannotProduceATree_SaysSoRatherThanBlamingThePage()
    {
        var snapshot = new AccessibilitySnapshot(
            Roots: [],
            IgnoredCount: 0,
            DiagnosticMessage: "Accessibility.getFullAXTree is not supported on the active transport "
                + "(Firefox/WebDriver BiDi). Use a Chromium-based browser for accessibility audits.");

        var service = new PageSnapshotService(new FakeAccessibilityPage(snapshot));

        var text = await service.TakeSnapshotAsync();

        StringAssert.Contains(text, "not supported on the active transport");
        StringAssert.Contains(text, "cannot be used with this browser");
        StringAssert.Contains(text, "click_xy");
        Assert.IsFalse(
            text.Contains("canvas", StringComparison.Ordinal),
            "the page is not the reason the tree is empty, so do not send the agent looking at it");
    }

    private static AccessibilitySnapshot EmptySnapshot()
        => new([], IgnoredCount: 0, DiagnosticMessage: null);

    /// <summary>
    /// Minimal <see cref="IPage"/> that only serves a fixed accessibility snapshot;
    /// every other member is unused by these tests.
    /// </summary>
    private sealed class FakeAccessibilityPage(AccessibilitySnapshot snapshot) : IPage
    {
        public Task<AccessibilitySnapshot> AccessibilitySnapshotAsync(CancellationToken ct = default)
            => Task.FromResult(snapshot);

        public Task<AccessibilityAuditResult> RunAccessibilityAuditAsync(CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<PerformanceMetrics?> GetPerformanceMetricsAsync(CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task StartHarRecordingAsync(CancellationToken ct = default) => throw new NotImplementedException();

        public Task StopHarRecordingAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<string> StartVideoRecordingAsync(string? path = null, ViewportSize? size = null, CancellationToken ct = default) => throw new NotImplementedException();

        public Task<string> StopVideoRecordingAsync(CancellationToken ct = default) => throw new NotImplementedException();

        public ILocator LocatorByBackendNodeId(long backendNodeId) => throw new NotImplementedException();

        /// <summary>The last selector this page was asked to build a locator for.</summary>
        public string? ResolvedSelector { get; private set; }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

#pragma warning disable CS0067 // events are part of the interface but unused in tests
        public event EventHandler? Close;
        public event EventHandler<ConsoleMessageEventArgs>? Console;
        public event EventHandler<DialogEventArgs>? Dialog;
        public event EventHandler<IDownload>? Download;
        public event EventHandler<IFileChooser>? FileChooser;
        public event EventHandler<PageErrorEventArgs>? PageError;
        public event EventHandler<IPage>? Popup;
        public event EventHandler<RequestEventArgs>? Request;
        public event EventHandler<RequestEventArgs>? RequestFailed;
        public event EventHandler<RequestEventArgs>? RequestFinished;
        public event EventHandler<ResponseEventArgs>? Response;
        public event EventHandler<IFrame>? FrameAttached;
        public event EventHandler<IFrame>? FrameNavigated;
        public event EventHandler<IFrame>? FrameDetached;
#pragma warning restore CS0067

        public IBrowserContext Context => throw new NotImplementedException();
        public IFrame MainFrame => throw new NotImplementedException();

        /// <summary>
        /// A page with no frames of its own. These tests are about how a tree is rendered and how
        /// its refs resolve, and a page-level snapshot reads this to decide whether to say that
        /// content is sitting in a frame it did not describe.
        /// </summary>
        public IReadOnlyList<IFrame> Frames => [];
        public string Url => throw new NotImplementedException();
        public IKeyboard Keyboard => throw new NotImplementedException();
        public IMouse Mouse => throw new NotImplementedException();
        public ITouchscreen Touchscreen => throw new NotImplementedException();
        public IVideo? Video => throw new NotImplementedException();
        public bool IsClosed => throw new NotImplementedException();
        public ViewportSize? ViewportSize => throw new NotImplementedException();

        public Task<IResponse?> GotoAsync(string url, NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<IResponse?> GoBackAsync(NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<IResponse?> GoForwardAsync(NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<IResponse?> ReloadAsync(NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<string> ContentAsync() => throw new NotImplementedException();
        public Task SetContentAsync(string html, NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<string> TitleAsync() => throw new NotImplementedException();
        public ILocator Locator(string selector, LocatorOptions? options = null)
        {
            ResolvedSelector = selector;
            return new FakeToolLocator();
        }

        public ILocator GetByRole(string role, string? name = null) => throw new NotImplementedException();
        public ILocator GetByText(string text, bool? exact = null) => throw new NotImplementedException();
        public ILocator GetByLabel(string text, bool? exact = null) => throw new NotImplementedException();
        public ILocator GetByPlaceholder(string text, bool? exact = null) => throw new NotImplementedException();
        public ILocator GetByTestId(string testId) => throw new NotImplementedException();
        public ILocator GetByTitle(string text, bool? exact = null) => throw new NotImplementedException();
        public ILocator GetByAltText(string text, bool? exact = null) => throw new NotImplementedException();
        public Task<T> EvaluateAsync<T>(string expression, object? arg = null) => throw new NotImplementedException();
        public Task<IJSHandle> EvaluateHandleAsync(string expression, object? arg = null) => throw new NotImplementedException();
        public Task<T> WaitForFunctionAsync<T>(string expression, object? arg = null, double? timeout = null) => throw new NotImplementedException();
        public Task WaitForLoadStateAsync(LoadState? state = null, double? timeout = null) => throw new NotImplementedException();
        public Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<IRequest> WaitForRequestAsync(string urlPattern, double? timeout = null) => throw new NotImplementedException();
        public Task<IResponse> WaitForResponseAsync(string urlPattern, double? timeout = null) => throw new NotImplementedException();
        public Task WaitForTimeoutAsync(double timeout) => throw new NotImplementedException();
        public Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null) => throw new NotImplementedException();
        public Task RouteAsync(string urlPattern, Func<IRoute, Task> handler) => throw new NotImplementedException();
        public Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null) => throw new NotImplementedException();
        public Task SetViewportSizeAsync(ViewportSize viewportSize) => throw new NotImplementedException();
        public Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null) => throw new NotImplementedException();
        public Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null) => throw new NotImplementedException();
        public Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback) => throw new NotImplementedException();
        public Task CloseAsync(bool? runBeforeUnload = null) => throw new NotImplementedException();
        public Task BringToFrontAsync() => throw new NotImplementedException();
        public Task PauseAsync() => throw new NotImplementedException();
        public Task<byte[]> PdfAsync(string? path = null) => throw new NotImplementedException();
        public Task EmulateMediaAsync(string? media = null, ColorScheme? colorScheme = null) => throw new NotImplementedException();
    }
}
