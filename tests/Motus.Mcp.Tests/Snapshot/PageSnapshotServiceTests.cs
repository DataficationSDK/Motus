using Motus.Abstractions;
using Motus.Mcp;

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

        public ILocator LocatorByBackendNodeId(long backendNodeId) => throw new NotImplementedException();

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
#pragma warning restore CS0067

        public IBrowserContext Context => throw new NotImplementedException();
        public IFrame MainFrame => throw new NotImplementedException();
        public IReadOnlyList<IFrame> Frames => throw new NotImplementedException();
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
        public ILocator Locator(string selector, LocatorOptions? options = null) => throw new NotImplementedException();
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
