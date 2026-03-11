namespace Motus.Abstractions;

/// <summary>
/// Represents a single browser page (tab). Provides methods for navigation, interaction, evaluation, and more.
/// </summary>
public interface IPage : IAsyncDisposable
{
    // --- Properties ---

    /// <summary>
    /// Gets the browser context that owns this page.
    /// </summary>
    IBrowserContext Context { get; }

    /// <summary>
    /// Gets the main frame of the page.
    /// </summary>
    IFrame MainFrame { get; }

    /// <summary>
    /// Gets all frames in the page, including the main frame.
    /// </summary>
    IReadOnlyList<IFrame> Frames { get; }

    /// <summary>
    /// Gets the current URL of the page.
    /// </summary>
    string Url { get; }

    /// <summary>
    /// Gets the keyboard input handler.
    /// </summary>
    IKeyboard Keyboard { get; }

    /// <summary>
    /// Gets the mouse input handler.
    /// </summary>
    IMouse Mouse { get; }

    /// <summary>
    /// Gets the touchscreen input handler.
    /// </summary>
    ITouchscreen Touchscreen { get; }

    /// <summary>
    /// Gets the video recorder for this page, or null if video recording is not enabled.
    /// </summary>
    IVideo? Video { get; }

    /// <summary>
    /// Gets whether the page has been closed.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Gets the viewport size, or null if no viewport is set.
    /// </summary>
    ViewportSize? ViewportSize { get; }

    // --- Events ---

    /// <summary>
    /// Raised when the page closes.
    /// </summary>
    event EventHandler? Close;

    /// <summary>
    /// Raised when a console message is logged.
    /// </summary>
    event EventHandler<ConsoleMessageEventArgs>? Console;

    /// <summary>
    /// Raised when a JavaScript dialog is opened.
    /// </summary>
    event EventHandler<DialogEventArgs>? Dialog;

    /// <summary>
    /// Raised when a download is started.
    /// </summary>
    event EventHandler<IDownload>? Download;

    /// <summary>
    /// Raised when a file chooser is opened.
    /// </summary>
    event EventHandler<IFileChooser>? FileChooser;

    /// <summary>
    /// Raised when an uncaught exception occurs in the page.
    /// </summary>
    event EventHandler<PageErrorEventArgs>? PageError;

    /// <summary>
    /// Raised when a popup page is opened.
    /// </summary>
    event EventHandler<IPage>? Popup;

    /// <summary>
    /// Raised when a network request is made.
    /// </summary>
    event EventHandler<RequestEventArgs>? Request;

    /// <summary>
    /// Raised when a network request fails.
    /// </summary>
    event EventHandler<RequestEventArgs>? RequestFailed;

    /// <summary>
    /// Raised when a network request finishes.
    /// </summary>
    event EventHandler<RequestEventArgs>? RequestFinished;

    /// <summary>
    /// Raised when a network response is received.
    /// </summary>
    event EventHandler<ResponseEventArgs>? Response;

    // --- Navigation ---

    /// <summary>
    /// Navigates the page to the specified URL.
    /// </summary>
    /// <param name="url">The URL to navigate to.</param>
    /// <param name="options">Navigation options.</param>
    /// <returns>The response of the navigation, or null.</returns>
    Task<IResponse?> GotoAsync(string url, NavigationOptions? options = null);

    /// <summary>
    /// Navigates back in the page history.
    /// </summary>
    /// <param name="options">Navigation options.</param>
    /// <returns>The response of the navigation, or null.</returns>
    Task<IResponse?> GoBackAsync(NavigationOptions? options = null);

    /// <summary>
    /// Navigates forward in the page history.
    /// </summary>
    /// <param name="options">Navigation options.</param>
    /// <returns>The response of the navigation, or null.</returns>
    Task<IResponse?> GoForwardAsync(NavigationOptions? options = null);

    /// <summary>
    /// Reloads the current page.
    /// </summary>
    /// <param name="options">Navigation options.</param>
    /// <returns>The response of the reload, or null.</returns>
    Task<IResponse?> ReloadAsync(NavigationOptions? options = null);

    // --- Content ---

    /// <summary>
    /// Returns the full HTML content of the page.
    /// </summary>
    /// <returns>The HTML content.</returns>
    Task<string> ContentAsync();

    /// <summary>
    /// Sets the HTML content of the page.
    /// </summary>
    /// <param name="html">The HTML content to set.</param>
    /// <param name="options">Navigation options.</param>
    Task SetContentAsync(string html, NavigationOptions? options = null);

    /// <summary>
    /// Returns the title of the page.
    /// </summary>
    /// <returns>The page title.</returns>
    Task<string> TitleAsync();

    // --- Locators ---

    /// <summary>
    /// Creates a locator for the specified selector.
    /// </summary>
    /// <param name="selector">The selector to locate elements.</param>
    /// <param name="options">Locator options.</param>
    /// <returns>A locator for the matching elements.</returns>
    ILocator Locator(string selector, LocatorOptions? options = null);

    /// <summary>
    /// Creates a locator for the element matching the specified role.
    /// </summary>
    /// <param name="role">The ARIA role to match.</param>
    /// <param name="name">Optional accessible name to filter by.</param>
    /// <returns>A locator for the matching elements.</returns>
    ILocator GetByRole(string role, string? name = null);

    /// <summary>
    /// Creates a locator for the element matching the specified text.
    /// </summary>
    /// <param name="text">The text to match.</param>
    /// <param name="exact">Whether to match the text exactly.</param>
    /// <returns>A locator for the matching elements.</returns>
    ILocator GetByText(string text, bool? exact = null);

    /// <summary>
    /// Creates a locator for the element matching the specified label.
    /// </summary>
    /// <param name="text">The label text to match.</param>
    /// <param name="exact">Whether to match the text exactly.</param>
    /// <returns>A locator for the matching elements.</returns>
    ILocator GetByLabel(string text, bool? exact = null);

    /// <summary>
    /// Creates a locator for the element matching the specified placeholder.
    /// </summary>
    /// <param name="text">The placeholder text to match.</param>
    /// <param name="exact">Whether to match the text exactly.</param>
    /// <returns>A locator for the matching elements.</returns>
    ILocator GetByPlaceholder(string text, bool? exact = null);

    /// <summary>
    /// Creates a locator for the element matching the specified test ID.
    /// </summary>
    /// <param name="testId">The test ID to match.</param>
    /// <returns>A locator for the matching elements.</returns>
    ILocator GetByTestId(string testId);

    /// <summary>
    /// Creates a locator for the element matching the specified title.
    /// </summary>
    /// <param name="text">The title text to match.</param>
    /// <param name="exact">Whether to match the text exactly.</param>
    /// <returns>A locator for the matching elements.</returns>
    ILocator GetByTitle(string text, bool? exact = null);

    /// <summary>
    /// Creates a locator for the element matching the specified alt text.
    /// </summary>
    /// <param name="text">The alt text to match.</param>
    /// <param name="exact">Whether to match the text exactly.</param>
    /// <returns>A locator for the matching elements.</returns>
    ILocator GetByAltText(string text, bool? exact = null);

    // --- Evaluation ---

    /// <summary>
    /// Evaluates a JavaScript expression in the page context.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="expression">The JavaScript expression to evaluate.</param>
    /// <param name="arg">Optional argument to pass to the expression.</param>
    /// <returns>The result of the evaluation.</returns>
    Task<T> EvaluateAsync<T>(string expression, object? arg = null);

    /// <summary>
    /// Evaluates a JavaScript expression and returns a handle to the result.
    /// </summary>
    /// <param name="expression">The JavaScript expression to evaluate.</param>
    /// <param name="arg">Optional argument to pass to the expression.</param>
    /// <returns>A handle to the result.</returns>
    Task<IJSHandle> EvaluateHandleAsync(string expression, object? arg = null);

    // --- Waiting ---

    /// <summary>
    /// Waits for a function to return a truthy value.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="expression">The JavaScript expression to evaluate.</param>
    /// <param name="arg">Optional argument to pass to the expression.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The return value of the function.</returns>
    Task<T> WaitForFunctionAsync<T>(string expression, object? arg = null, double? timeout = null);

    /// <summary>
    /// Waits for the specified load state.
    /// </summary>
    /// <param name="state">The load state to wait for.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task WaitForLoadStateAsync(LoadState? state = null, double? timeout = null);

    /// <summary>
    /// Waits for a matching URL navigation.
    /// </summary>
    /// <param name="urlPattern">The URL pattern to match.</param>
    /// <param name="options">Navigation options.</param>
    Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null);

    /// <summary>
    /// Waits for a network request matching the URL pattern.
    /// </summary>
    /// <param name="urlPattern">The URL pattern to match.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The matching request.</returns>
    Task<IRequest> WaitForRequestAsync(string urlPattern, double? timeout = null);

    /// <summary>
    /// Waits for a network response matching the URL pattern.
    /// </summary>
    /// <param name="urlPattern">The URL pattern to match.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The matching response.</returns>
    Task<IResponse> WaitForResponseAsync(string urlPattern, double? timeout = null);

    /// <summary>
    /// Waits for a specified amount of time.
    /// </summary>
    /// <param name="timeout">The time to wait in milliseconds.</param>
    Task WaitForTimeoutAsync(double timeout);

    // --- Screenshots ---

    /// <summary>
    /// Takes a screenshot of the page.
    /// </summary>
    /// <param name="options">Screenshot options.</param>
    /// <returns>The screenshot as a byte array.</returns>
    Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null);

    // --- Routing ---

    /// <summary>
    /// Registers a route handler for requests matching the URL pattern.
    /// </summary>
    /// <param name="urlPattern">The URL pattern to match.</param>
    /// <param name="handler">The route handler.</param>
    Task RouteAsync(string urlPattern, Func<IRoute, Task> handler);

    /// <summary>
    /// Removes a previously registered route handler.
    /// </summary>
    /// <param name="urlPattern">The URL pattern to unregister.</param>
    /// <param name="handler">The handler to remove. If null, removes all handlers for the pattern.</param>
    Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null);

    // --- Misc ---

    /// <summary>
    /// Sets the viewport size.
    /// </summary>
    /// <param name="viewportSize">The new viewport size.</param>
    Task SetViewportSizeAsync(ViewportSize viewportSize);

    /// <summary>
    /// Adds a script tag to the page.
    /// </summary>
    /// <param name="url">URL of the script to add.</param>
    /// <param name="content">Inline script content.</param>
    /// <returns>An element handle to the script element.</returns>
    Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null);

    /// <summary>
    /// Adds a style tag to the page.
    /// </summary>
    /// <param name="url">URL of the stylesheet to add.</param>
    /// <param name="content">Inline CSS content.</param>
    /// <returns>An element handle to the style element.</returns>
    Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null);

    /// <summary>
    /// Exposes a .NET function to be called from JavaScript.
    /// </summary>
    /// <param name="name">The function name to expose in the page.</param>
    /// <param name="callback">The callback to invoke.</param>
    Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback);

    /// <summary>
    /// Closes the page.
    /// </summary>
    /// <param name="runBeforeUnload">Whether to run the beforeunload handler.</param>
    Task CloseAsync(bool? runBeforeUnload = null);

    /// <summary>
    /// Brings the page to the front (activates the tab).
    /// </summary>
    Task BringToFrontAsync();

    /// <summary>
    /// Pauses script execution. Useful for debugging.
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Generates a PDF of the page. Only supported in headless Chromium.
    /// </summary>
    /// <param name="path">Optional path to save the PDF to.</param>
    /// <returns>The PDF as a byte array.</returns>
    Task<byte[]> PdfAsync(string? path = null);

    /// <summary>
    /// Emulates the given media type or features.
    /// </summary>
    /// <param name="media">The media type (e.g. "screen", "print").</param>
    /// <param name="colorScheme">The color scheme to emulate.</param>
    Task EmulateMediaAsync(string? media = null, ColorScheme? colorScheme = null);
}
