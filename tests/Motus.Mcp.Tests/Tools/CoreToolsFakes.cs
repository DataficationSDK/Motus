using System.Linq;
using System.Text.Json;
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

    /// <summary>The page-level keyboard, recording the keys <c>press_key</c> sends.</summary>
    public FakeKeyboard FakeKeyboard { get; } = new();

    /// <summary>The last timeout passed to <see cref="WaitForTimeoutAsync"/>.</summary>
    public double? WaitedTimeoutMs { get; private set; }

    /// <summary>The function expressions passed to <see cref="WaitForFunctionAsync{T}"/>.</summary>
    public List<string> WaitedFunctions { get; } = [];

    /// <summary>The arguments passed alongside each <see cref="WaitForFunctionAsync{T}"/> call.</summary>
    public List<object?> WaitedFunctionArgs { get; } = [];

    /// <summary>The current URL, returned by the <see cref="Url"/> property.</summary>
    public string PageUrl { get; set; } = "about:blank";

    /// <summary>The title returned by <see cref="TitleAsync"/>.</summary>
    public string PageTitle { get; set; } = "";

    /// <summary>The response <see cref="GoBackAsync"/> returns; null models no history entry.</summary>
    public IResponse? GoBackResponse { get; set; }

    /// <summary>The response <see cref="GoForwardAsync"/> returns; null models no history entry.</summary>
    public IResponse? GoForwardResponse { get; set; }

    /// <summary>Whether <see cref="GoBackAsync"/> was called.</summary>
    public bool GoBackCalled { get; private set; }

    /// <summary>Whether <see cref="GoForwardAsync"/> was called.</summary>
    public bool GoForwardCalled { get; private set; }

    /// <summary>Whether <see cref="ReloadAsync"/> was called.</summary>
    public bool ReloadCalled { get; private set; }

    /// <summary>The number of times <see cref="BringToFrontAsync"/> was called.</summary>
    public int BringToFrontCount { get; private set; }

    /// <summary>Whether <see cref="CloseAsync"/> was called.</summary>
    public bool CloseCalled { get; private set; }

    /// <summary>The last expression passed to <see cref="EvaluateAsync{T}"/>.</summary>
    public string? EvaluatedExpression { get; private set; }

    /// <summary>The value <see cref="EvaluateAsync{T}"/> returns when asked for a <see cref="JsonElement"/>.</summary>
    public JsonElement EvaluateReturn { get; set; }

    /// <summary>When set, <see cref="EvaluateAsync{T}"/> throws this to simulate a script error.</summary>
    public Exception? EvaluateError { get; init; }

    private bool _closed;

    /// <summary>Raises the <see cref="Dialog"/> event with the given dialog, as the browser would.</summary>
    public void RaiseDialog(IDialog dialog) => Dialog?.Invoke(this, new DialogEventArgs(dialog));

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

    public bool IsClosed => _closed;

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
    public string Url => PageUrl;
    public IKeyboard Keyboard => FakeKeyboard;
    public IMouse Mouse => throw new NotImplementedException();
    public ITouchscreen Touchscreen => throw new NotImplementedException();
    public IVideo? Video => throw new NotImplementedException();
    public ViewportSize? ViewportSize => throw new NotImplementedException();

    public Task<IResponse?> GoBackAsync(NavigationOptions? options = null)
    {
        GoBackCalled = true;
        return Task.FromResult(GoBackResponse);
    }
    public Task<IResponse?> GoForwardAsync(NavigationOptions? options = null)
    {
        GoForwardCalled = true;
        return Task.FromResult(GoForwardResponse);
    }
    public Task<IResponse?> ReloadAsync(NavigationOptions? options = null)
    {
        ReloadCalled = true;
        return Task.FromResult<IResponse?>(null);
    }
    public Task<string> ContentAsync() => throw new NotImplementedException();
    public Task SetContentAsync(string html, NavigationOptions? options = null) => throw new NotImplementedException();
    public Task<string> TitleAsync() => Task.FromResult(PageTitle);
    public ILocator Locator(string selector, LocatorOptions? options = null) => throw new NotImplementedException();
    public ILocator GetByRole(string role, string? name = null) => throw new NotImplementedException();
    public ILocator GetByText(string text, bool? exact = null) => throw new NotImplementedException();
    public ILocator GetByLabel(string text, bool? exact = null) => throw new NotImplementedException();
    public ILocator GetByPlaceholder(string text, bool? exact = null) => throw new NotImplementedException();
    public ILocator GetByTestId(string testId) => throw new NotImplementedException();
    public ILocator GetByTitle(string text, bool? exact = null) => throw new NotImplementedException();
    public ILocator GetByAltText(string text, bool? exact = null) => throw new NotImplementedException();
    public Task<T> EvaluateAsync<T>(string expression, object? arg = null)
    {
        EvaluatedExpression = expression;
        if (EvaluateError is not null)
            throw EvaluateError;
        if (typeof(T) == typeof(JsonElement))
            return Task.FromResult((T)(object)EvaluateReturn);
        throw new NotImplementedException();
    }
    public Task<IJSHandle> EvaluateHandleAsync(string expression, object? arg = null) => throw new NotImplementedException();
    public Task<T> WaitForFunctionAsync<T>(string expression, object? arg = null, double? timeout = null)
    {
        WaitedFunctions.Add(expression);
        WaitedFunctionArgs.Add(arg);
        return Task.FromResult<T>(default!);
    }

    public Task WaitForLoadStateAsync(LoadState? state = null, double? timeout = null) => throw new NotImplementedException();
    public Task<IResponse?> WaitForNavigationAsync(NavigationOptions? options = null) => throw new NotImplementedException();
    public Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null) => throw new NotImplementedException();
    public Task<IRequest> WaitForRequestAsync(string urlPattern, double? timeout = null) => throw new NotImplementedException();
    public Task<IResponse> WaitForResponseAsync(string urlPattern, double? timeout = null) => throw new NotImplementedException();
    public Task<IRequest> WaitForRequestAsync(Func<IRequest, bool> predicate, double? timeout = null) => throw new NotImplementedException();
    public Task<IResponse> WaitForResponseAsync(Func<IResponse, bool> predicate, double? timeout = null) => throw new NotImplementedException();
    public Task<IPage> WaitForPopupAsync(double? timeout = null) => throw new NotImplementedException();
    public Task WaitForTimeoutAsync(double timeout)
    {
        WaitedTimeoutMs = timeout;
        return Task.CompletedTask;
    }
    public Task RouteAsync(string urlPattern, Func<IRoute, Task> handler) => throw new NotImplementedException();
    public Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null) => throw new NotImplementedException();
    public Task AddInitScriptAsync(string script) => throw new NotImplementedException();
    public Task SetViewportSizeAsync(ViewportSize viewportSize) => throw new NotImplementedException();
    public Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null) => throw new NotImplementedException();
    public Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null) => throw new NotImplementedException();
    public Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback) => throw new NotImplementedException();
    public Task CloseAsync(bool? runBeforeUnload = null)
    {
        CloseCalled = true;
        _closed = true;
        return Task.CompletedTask;
    }
    public Task BringToFrontAsync()
    {
        BringToFrontCount++;
        return Task.CompletedTask;
    }
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
    public int HoverCount { get; private set; }
    public int ClearCount { get; private set; }
    public int FocusCount { get; private set; }
    public int ScrollIntoViewCount { get; private set; }
    public bool? CheckedValue { get; private set; }
    public IReadOnlyList<string>? SelectedValues { get; private set; }
    public IReadOnlyList<FilePayload>? UploadedFiles { get; private set; }
    public ElementState? WaitedForState { get; private set; }
    public string? EvaluatedElementExpression { get; private set; }
    public JsonElement ElementEvaluateReturn { get; set; }

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

    public Task SetCheckedAsync(bool @checked, double? timeout = null)
    {
        CheckedValue = @checked;
        return Task.CompletedTask;
    }

    public Task ClearAsync(double? timeout = null)
    {
        ClearCount++;
        return Task.CompletedTask;
    }

    public Task FocusAsync(double? timeout = null)
    {
        FocusCount++;
        return Task.CompletedTask;
    }

    public Task HoverAsync(double? timeout = null)
    {
        HoverCount++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> SelectOptionAsync(params string[] values)
    {
        SelectedValues = values;
        return Task.FromResult<IReadOnlyList<string>>(values);
    }

    public Task SetInputFilesAsync(IEnumerable<FilePayload> files, double? timeout = null)
    {
        UploadedFiles = files.ToList();
        return Task.CompletedTask;
    }

    public Task TapAsync(double? timeout = null) => throw new NotImplementedException();

    public Task ScrollIntoViewIfNeededAsync(double? timeout = null)
    {
        ScrollIntoViewCount++;
        return Task.CompletedTask;
    }
    public Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null) => throw new NotImplementedException();
    public Task DispatchEventAsync(string type, object? eventInit = null) => throw new NotImplementedException();
    public Task<T> EvaluateAsync<T>(string expression, object? arg = null) => throw new NotImplementedException();
    public Task<T> EvaluateWithElementAsync<T>(string pageFunction, object? arg = null)
    {
        EvaluatedElementExpression = pageFunction;
        if (typeof(T) == typeof(JsonElement))
            return Task.FromResult((T)(object)ElementEvaluateReturn);
        throw new NotImplementedException();
    }
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
    public Task WaitForAsync(ElementState? state = null, double? timeout = null)
    {
        WaitedForState = state;
        return Task.CompletedTask;
    }
    public Task<IElementHandle> ElementHandleAsync(double? timeout = null) => throw new NotImplementedException();
    public Task<IReadOnlyList<IElementHandle>> ElementHandlesAsync() => throw new NotImplementedException();
}

/// <summary>
/// A fake page-level keyboard that records the keys <c>press_key</c> sends and
/// no-ops the rest.
/// </summary>
internal sealed class FakeKeyboard : IKeyboard
{
    public List<string> PressedKeys { get; } = [];

    public Task PressAsync(string key, KeyboardPressOptions? options = null)
    {
        PressedKeys.Add(key);
        return Task.CompletedTask;
    }

    public Task DownAsync(string key) => throw new NotImplementedException();
    public Task UpAsync(string key) => throw new NotImplementedException();
    public Task TypeAsync(string text, KeyboardTypeOptions? options = null) => throw new NotImplementedException();
    public Task InsertTextAsync(string text) => throw new NotImplementedException();
}

/// <summary>
/// A fake dialog that records whether it was accepted (with what text) or dismissed.
/// </summary>
internal sealed class FakeDialog(DialogType type = DialogType.Alert, string message = "", string? defaultValue = null)
    : IDialog
{
    public DialogType Type { get; } = type;
    public string Message { get; } = message;
    public string? DefaultValue { get; } = defaultValue;
    public bool Accepted { get; private set; }
    public bool Dismissed { get; private set; }
    public string? AcceptedText { get; private set; }

    public Task AcceptAsync(string? promptText = null)
    {
        Accepted = true;
        AcceptedText = promptText;
        return Task.CompletedTask;
    }

    public Task DismissAsync()
    {
        Dismissed = true;
        return Task.CompletedTask;
    }
}

/// <summary>
/// An <see cref="ActivePageService"/> for the tab and context tools. It overrides the
/// browser-touching seams to work over an in-memory list of fake tabs and a simulated
/// set of context names, so the tools' own index validation, active-page tracking, and
/// error mapping run for real without a browser.
/// </summary>
internal sealed class FakeSessionPageService : ActivePageService
{
    private readonly List<FakeToolPage> _pages;

    public FakeSessionPageService(params FakeToolPage[] pages)
        : base(new BrowserSessionManager(new McpServerLaunchOptions()))
    {
        _pages = pages.Length == 0 ? [NewPage()] : [.. pages];
    }

    /// <summary>The simulated open context names; the first is the implicit default.</summary>
    public List<string> Contexts { get; } = [BrowserSessionManager.DefaultContextName];

    /// <summary>The simulated active context name.</summary>
    public string ActiveContext { get; private set; } = BrowserSessionManager.DefaultContextName;

    public List<string> CreatedContexts { get; } = [];
    public List<string> SelectedContexts { get; } = [];
    public List<string> ClosedContexts { get; } = [];
    public int OpenedTabs { get; private set; }

    public IReadOnlyList<FakeToolPage> Tabs => _pages;

    private static FakeToolPage NewPage() => new(new AccessibilitySnapshot([], 0, null));

    protected override Task<IPage> ResolvePageAsync(CancellationToken cancellationToken)
    {
        var open = _pages.FirstOrDefault(p => !p.IsClosed) ?? AddTab();
        return Task.FromResult<IPage>(open);
    }

    protected override Task<IReadOnlyList<IPage>> GetActiveContextPagesAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<IPage>>(_pages.Where(p => !p.IsClosed).Cast<IPage>().ToArray());

    public override Task<IPage> OpenNewTabAsync(CancellationToken cancellationToken = default)
    {
        OpenedTabs++;
        var page = AddTab();
        SelectPage(page);
        return Task.FromResult<IPage>(page);
    }

    public override Task CreateContextAsync(string name, CancellationToken cancellationToken = default)
    {
        if (Contexts.Contains(name))
            throw new InvalidOperationException($"A context named '{name}' already exists.");

        Contexts.Add(name);
        ActiveContext = name;
        CreatedContexts.Add(name);
        ResetActivePage();
        return Task.CompletedTask;
    }

    public override void SelectContext(string name)
    {
        if (!Contexts.Contains(name))
            throw new InvalidOperationException($"No open context named '{name}'.");

        ActiveContext = name;
        SelectedContexts.Add(name);
        ResetActivePage();
    }

    public override Task CloseContextAsync(string name, CancellationToken cancellationToken = default)
    {
        Contexts.Remove(name);
        if (ActiveContext == name)
            ActiveContext = BrowserSessionManager.DefaultContextName;

        ClosedContexts.Add(name);
        ResetActivePage();
        return Task.CompletedTask;
    }

    public override IReadOnlyCollection<string> GetContextNames() => Contexts;

    public override string GetActiveContextName() => ActiveContext;

    private FakeToolPage AddTab()
    {
        var page = NewPage();
        _pages.Add(page);
        return page;
    }
}
