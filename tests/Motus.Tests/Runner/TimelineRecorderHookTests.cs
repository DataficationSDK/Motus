using Motus.Abstractions;
using Motus.Runner.Services.Timeline;

namespace Motus.Tests.Runner;

[TestClass]
public class TimelineRecorderHookTests
{
    [TestMethod]
    public async Task RecordsEntry_AfterAction()
    {
        var timeline = new TimelineService();
        var stepDebug = new StepDebugService();
        var hook = new TimelineRecorderHook(timeline, stepDebug);
        var page = new FakePage();

        await hook.BeforeActionAsync(page, "click");
        await hook.AfterActionAsync(page, "click", new ActionResult("click"));

        Assert.AreEqual(1, timeline.Entries.Count);
        var entry = timeline.Entries[0];
        Assert.AreEqual("click", entry.ActionType);
        Assert.IsFalse(entry.HasError);
        Assert.IsTrue(entry.Duration >= TimeSpan.Zero);
    }

    [TestMethod]
    public async Task RecordsError_WhenActionFails()
    {
        var timeline = new TimelineService();
        var stepDebug = new StepDebugService();
        var hook = new TimelineRecorderHook(timeline, stepDebug);
        var page = new FakePage();

        await hook.BeforeActionAsync(page, "fill");
        var error = new InvalidOperationException("Element not found");
        await hook.AfterActionAsync(page, "fill", new ActionResult("fill", error));

        Assert.AreEqual(1, timeline.Entries.Count);
        var entry = timeline.Entries[0];
        Assert.IsTrue(entry.HasError);
        Assert.AreEqual("Element not found", entry.ErrorMessage);
    }

    [TestMethod]
    public async Task NavigationEntry_Recorded()
    {
        var timeline = new TimelineService();
        var stepDebug = new StepDebugService();
        var hook = new TimelineRecorderHook(timeline, stepDebug);
        var page = new FakePage();

        await hook.BeforeNavigationAsync(page, "https://example.com");
        await hook.AfterNavigationAsync(page, null);

        Assert.AreEqual(1, timeline.Entries.Count);
        Assert.AreEqual("navigate", timeline.Entries[0].ActionType);
    }

    /// <summary>
    /// Minimal IPage stub. Only ScreenshotAsync and Url are used by the hook.
    /// </summary>
    private sealed class FakePage : IPage
    {
        public string Url => "https://test.local";
        public IBrowserContext Context => null!;
        public IKeyboard Keyboard => null!;
        public IMouse Mouse => null!;
        public ITouchscreen Touchscreen => null!;
        public IFrame MainFrame => null!;
        public IReadOnlyList<IFrame> Frames => [];
        public ViewportSize? ViewportSize => null;
        public bool IsClosed => false;
        public IVideo? Video => null;

        public event EventHandler<IPage>? Popup;
        public event EventHandler<ConsoleMessageEventArgs>? Console;
        public event EventHandler<PageErrorEventArgs>? PageError;
        public event EventHandler<RequestEventArgs>? Request;
        public event EventHandler<RequestEventArgs>? RequestFailed;
        public event EventHandler<RequestEventArgs>? RequestFinished;
        public event EventHandler<ResponseEventArgs>? Response;
        public event EventHandler<DialogEventArgs>? Dialog;
        public event EventHandler<IDownload>? Download;
        public event EventHandler<IFileChooser>? FileChooser;
        public event EventHandler? Load;
        public event EventHandler? DOMContentLoaded;
        public event EventHandler? Close;

        public Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null)
            => Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47 });

        public Task<IResponse?> GotoAsync(string url, NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<IResponse?> ReloadAsync(NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<IResponse?> GoBackAsync(NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<IResponse?> GoForwardAsync(NavigationOptions? options = null) => throw new NotImplementedException();
        public Task WaitForLoadStateAsync(LoadState? state = null, double? timeout = null) => throw new NotImplementedException();
        public Task<IResponse?> WaitForNavigationAsync(NavigationOptions? options = null) => throw new NotImplementedException();
        public Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null) => throw new NotImplementedException();
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
        public Task<IRequest> WaitForRequestAsync(string urlPattern, double? timeout = null) => throw new NotImplementedException();
        public Task<IResponse> WaitForResponseAsync(string urlPattern, double? timeout = null) => throw new NotImplementedException();
        public Task WaitForTimeoutAsync(double timeout) => throw new NotImplementedException();
        public Task<string> ContentAsync() => throw new NotImplementedException();
        public Task SetContentAsync(string html, NavigationOptions? options = null) => throw new NotImplementedException();
        public Task<string> TitleAsync() => throw new NotImplementedException();
        public Task SetViewportSizeAsync(ViewportSize viewportSize) => throw new NotImplementedException();
        public Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null) => throw new NotImplementedException();
        public Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null) => throw new NotImplementedException();
        public Task RouteAsync(string urlPattern, Func<IRoute, Task> handler) => throw new NotImplementedException();
        public Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null) => throw new NotImplementedException();
        public Task AddInitScriptAsync(string script) => throw new NotImplementedException();
        public Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback) => throw new NotImplementedException();
        public Task<IPage> WaitForPopupAsync(double? timeout = null) => throw new NotImplementedException();
        public Task<IResponse> WaitForResponseAsync(Func<IResponse, bool> predicate, double? timeout = null) => throw new NotImplementedException();
        public Task<IRequest> WaitForRequestAsync(Func<IRequest, bool> predicate, double? timeout = null) => throw new NotImplementedException();
        public Task<byte[]> PdfAsync(string? path = null) => throw new NotImplementedException();
        public Task BringToFrontAsync() => throw new NotImplementedException();
        public Task CloseAsync(bool? runBeforeUnload = null) => throw new NotImplementedException();
        public Task PauseAsync() => throw new NotImplementedException();
        public Task EmulateMediaAsync(string? media = null, ColorScheme? colorScheme = null) => throw new NotImplementedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
