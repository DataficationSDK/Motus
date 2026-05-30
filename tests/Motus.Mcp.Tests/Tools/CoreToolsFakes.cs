using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// An <see cref="ActivePageService"/> that serves a fixed fake page, so the tools
/// can be exercised without a browser. The snapshot registry, gating, and
/// invalidation run for real against the fake page.
/// </summary>
internal sealed class FakeActivePageService(FakeToolPage page)
    : ActivePageService(new BrowserSessionManager(new McpServerLaunchOptions()))
{
    public FakeToolPage Page { get; } = page;

    protected override Task<IPage> ResolvePageAsync(CancellationToken cancellationToken)
        => Task.FromResult<IPage>(Page);
}

/// <summary>
/// A fake page that serves a fixed accessibility snapshot, hands out a single
/// recording locator for any backend node id, and records navigation and
/// screenshot calls. Every other member is unused.
/// </summary>
internal sealed class FakeToolPage(AccessibilitySnapshot snapshot) : IPage
{
    public FakeToolLocator RecordingLocator { get; } = new();

    /// <summary>The last URL passed to <see cref="GotoAsync"/>.</summary>
    public string? NavigatedUrl { get; private set; }

    /// <summary>The last backend node id resolved through <see cref="LocatorByBackendNodeId"/>.</summary>
    public long? ResolvedBackendNodeId { get; private set; }

    /// <summary>The <c>FullPage</c> flag of the last screenshot request.</summary>
    public bool? ScreenshotFullPage { get; private set; }

    /// <summary>When set, <see cref="GotoAsync"/> throws this to simulate a failed navigation.</summary>
    public Exception? GotoError { get; init; }

    public Task<AccessibilitySnapshot> AccessibilitySnapshotAsync(CancellationToken ct = default)
        => Task.FromResult(snapshot);

    public ILocator LocatorByBackendNodeId(long backendNodeId)
    {
        ResolvedBackendNodeId = backendNodeId;
        return RecordingLocator;
    }

    public Task<IResponse?> GotoAsync(string url, NavigationOptions? options = null)
    {
        if (GotoError is not null)
            throw GotoError;

        NavigatedUrl = url;
        return Task.FromResult<IResponse?>(null);
    }

    public Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null)
    {
        ScreenshotFullPage = options?.FullPage;
        return Task.FromResult(new byte[] { 0x89, 0x50, 0x4E, 0x47 });
    }

    public bool IsClosed => false;

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
    public event EventHandler? Load;
    public event EventHandler? DOMContentLoaded;
#pragma warning restore CS0067

    public IBrowserContext Context => throw new NotImplementedException();
    public IFrame MainFrame => throw new NotImplementedException();
    public IReadOnlyList<IFrame> Frames => throw new NotImplementedException();
    public string Url => throw new NotImplementedException();
    public IKeyboard Keyboard => throw new NotImplementedException();
    public IMouse Mouse => throw new NotImplementedException();
    public ITouchscreen Touchscreen => throw new NotImplementedException();
    public IVideo? Video => throw new NotImplementedException();
    public ViewportSize? ViewportSize => throw new NotImplementedException();

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
    public Task<IResponse?> WaitForNavigationAsync(NavigationOptions? options = null) => throw new NotImplementedException();
    public Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null) => throw new NotImplementedException();
    public Task<IRequest> WaitForRequestAsync(string urlPattern, double? timeout = null) => throw new NotImplementedException();
    public Task<IResponse> WaitForResponseAsync(string urlPattern, double? timeout = null) => throw new NotImplementedException();
    public Task<IRequest> WaitForRequestAsync(Func<IRequest, bool> predicate, double? timeout = null) => throw new NotImplementedException();
    public Task<IResponse> WaitForResponseAsync(Func<IResponse, bool> predicate, double? timeout = null) => throw new NotImplementedException();
    public Task<IPage> WaitForPopupAsync(double? timeout = null) => throw new NotImplementedException();
    public Task WaitForTimeoutAsync(double timeout) => throw new NotImplementedException();
    public Task RouteAsync(string urlPattern, Func<IRoute, Task> handler) => throw new NotImplementedException();
    public Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null) => throw new NotImplementedException();
    public Task AddInitScriptAsync(string script) => throw new NotImplementedException();
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

/// <summary>
/// A fake locator that records the actions the tools invoke and no-ops the rest.
/// </summary>
internal sealed class FakeToolLocator : ILocator
{
    public int ClickCount { get; private set; }
    public int DblClickCount { get; private set; }
    public string? FilledValue { get; private set; }
    public string? TypedValue { get; private set; }
    public List<string> PressedKeys { get; } = [];

    public Task ClickAsync(double? timeout = null)
    {
        ClickCount++;
        return Task.CompletedTask;
    }

    public Task DblClickAsync(double? timeout = null)
    {
        DblClickCount++;
        return Task.CompletedTask;
    }

    public Task FillAsync(string value, double? timeout = null)
    {
        FilledValue = value;
        return Task.CompletedTask;
    }

    public Task TypeAsync(string text, KeyboardTypeOptions? options = null)
    {
        TypedValue = text;
        return Task.CompletedTask;
    }

    public Task PressAsync(string key, KeyboardPressOptions? options = null)
    {
        PressedKeys.Add(key);
        return Task.CompletedTask;
    }

    public Task CheckAsync(double? timeout = null) => throw new NotImplementedException();
    public Task UncheckAsync(double? timeout = null) => throw new NotImplementedException();
    public Task SetCheckedAsync(bool @checked, double? timeout = null) => throw new NotImplementedException();
    public Task ClearAsync(double? timeout = null) => throw new NotImplementedException();
    public Task FocusAsync(double? timeout = null) => throw new NotImplementedException();
    public Task HoverAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<IReadOnlyList<string>> SelectOptionAsync(params string[] values) => throw new NotImplementedException();
    public Task SetInputFilesAsync(IEnumerable<FilePayload> files, double? timeout = null) => throw new NotImplementedException();
    public Task TapAsync(double? timeout = null) => throw new NotImplementedException();
    public Task ScrollIntoViewIfNeededAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null) => throw new NotImplementedException();
    public Task DispatchEventAsync(string type, object? eventInit = null) => throw new NotImplementedException();
    public Task<T> EvaluateAsync<T>(string expression, object? arg = null) => throw new NotImplementedException();
    public Task<T> EvaluateWithElementAsync<T>(string pageFunction, object? arg = null) => throw new NotImplementedException();
    public Task<string?> TextContentAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<string> InnerTextAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<string> InnerHTMLAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<string?> GetAttributeAsync(string name, double? timeout = null) => throw new NotImplementedException();
    public Task<string> InputValueAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<BoundingBox?> BoundingBoxAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<int> CountAsync() => throw new NotImplementedException();
    public Task<IReadOnlyList<string>> AllInnerTextsAsync() => throw new NotImplementedException();
    public Task<IReadOnlyList<string>> AllTextContentsAsync() => throw new NotImplementedException();
    public Task<bool> IsCheckedAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<bool> IsDisabledAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<bool> IsEditableAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<bool> IsEnabledAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<bool> IsHiddenAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<bool> IsVisibleAsync(double? timeout = null) => throw new NotImplementedException();
    public ILocator First => throw new NotImplementedException();
    public ILocator Last => throw new NotImplementedException();
    public ILocator Nth(int index) => throw new NotImplementedException();
    public ILocator Filter(LocatorOptions? options = null) => throw new NotImplementedException();
    public ILocator Locator(string selector, LocatorOptions? options = null) => throw new NotImplementedException();
    public Task WaitForAsync(ElementState? state = null, double? timeout = null) => throw new NotImplementedException();
    public Task<IElementHandle> ElementHandleAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<IReadOnlyList<IElementHandle>> ElementHandlesAsync() => throw new NotImplementedException();
}
