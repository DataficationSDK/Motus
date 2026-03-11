namespace Motus.Abstractions;

/// <summary>
/// Represents a frame within a page (including the main frame).
/// </summary>
public interface IFrame
{
    /// <summary>
    /// Gets the page that owns this frame.
    /// </summary>
    IPage Page { get; }

    /// <summary>
    /// Gets the parent frame, or null if this is the main frame.
    /// </summary>
    IFrame? ParentFrame { get; }

    /// <summary>
    /// Gets the frame name as specified in the tag.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the current URL of the frame.
    /// </summary>
    string Url { get; }

    /// <summary>
    /// Gets the child frames.
    /// </summary>
    IReadOnlyList<IFrame> ChildFrames { get; }

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

    /// <summary>
    /// Navigates the frame to the specified URL.
    /// </summary>
    /// <param name="url">The URL to navigate to.</param>
    /// <param name="options">Navigation options.</param>
    /// <returns>The response of the navigation, or null.</returns>
    Task<IResponse?> GotoAsync(string url, NavigationOptions? options = null);

    /// <summary>
    /// Returns the full HTML content of the frame.
    /// </summary>
    /// <returns>The HTML content.</returns>
    Task<string> ContentAsync();

    /// <summary>
    /// Sets the HTML content of the frame.
    /// </summary>
    /// <param name="html">The HTML content to set.</param>
    /// <param name="options">Navigation options.</param>
    Task SetContentAsync(string html, NavigationOptions? options = null);

    /// <summary>
    /// Returns the title of the frame.
    /// </summary>
    /// <returns>The frame title.</returns>
    Task<string> TitleAsync();

    /// <summary>
    /// Evaluates a JavaScript expression in the frame context.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="expression">The JavaScript expression to evaluate.</param>
    /// <param name="arg">Optional argument to pass to the expression.</param>
    /// <returns>The result of the evaluation.</returns>
    Task<T> EvaluateAsync<T>(string expression, object? arg = null);

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
    /// Adds a script tag to the frame.
    /// </summary>
    /// <param name="url">URL of the script to add.</param>
    /// <param name="content">Inline script content.</param>
    /// <returns>An element handle to the script element.</returns>
    Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null);

    /// <summary>
    /// Adds a style tag to the frame.
    /// </summary>
    /// <param name="url">URL of the stylesheet to add.</param>
    /// <param name="content">Inline CSS content.</param>
    /// <returns>An element handle to the style element.</returns>
    Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null);
}
