using System.Text.RegularExpressions;

namespace Motus.Abstractions;

/// <summary>
/// Represents a frame within a page (including the main frame).
/// </summary>
/// <remarks>
/// Locators created from a frame resolve inside that frame. A selector that matches in one frame
/// and in another matches only the frame it was created from.
/// </remarks>
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
    /// Gets the child frames, in the order they attached, which is the order they appear in the
    /// document unless the page inserted one later.
    /// </summary>
    IReadOnlyList<IFrame> ChildFrames { get; }

    /// <summary>
    /// Gets whether the frame has been removed from its page.
    /// </summary>
    /// <remarks>
    /// A detached frame is a handle to something that no longer exists. Operations on it fail, and
    /// this is how to tell that apart from a selector that simply did not match.
    /// </remarks>
    bool IsDetached { get; }

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
    /// Navigates the frame to the specified URL, leaving the rest of the page where it is.
    /// </summary>
    /// <param name="url">The URL to navigate to.</param>
    /// <param name="options">Navigation options.</param>
    /// <returns>The response of the navigation, or null.</returns>
    /// <remarks>
    /// A frame other than the main frame reports only that it has finished loading, with no
    /// separate signal for its DOM being ready, so <see cref="WaitUntil.DOMContentLoaded"/> and
    /// <see cref="WaitUntil.Load"/> both wait for the same point.
    /// <see cref="WaitUntil.NetworkIdle"/> is measured across the whole page rather than for this
    /// frame alone. Navigating the main frame navigates the page.
    /// </remarks>
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
    /// Evaluates a JavaScript expression in the frame, in the specified world.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="expression">The JavaScript expression to evaluate.</param>
    /// <param name="arg">Optional argument to pass to the expression.</param>
    /// <param name="options">Evaluation options, including which world to run in.</param>
    /// <returns>The result of the evaluation.</returns>
    /// <remarks>
    /// An isolated world shares the frame's document but not its globals, so the DOM is fully
    /// present while anything the application defined on <c>window</c> is not. Reading application
    /// state needs <see cref="ExecutionWorld.Main"/>, which is what the overload without options
    /// uses.
    /// </remarks>
    Task<T> EvaluateAsync<T>(string expression, object? arg, EvaluateOptions options);

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
    /// Waits until this frame's URL matches <paramref name="urlPattern"/>, returning at once when it
    /// already does.
    /// </summary>
    /// <param name="urlPattern">
    /// The URL to wait for. A string containing <c>*</c> is a glob that has to cover the whole URL,
    /// where each <c>*</c> stands for any run of characters, path separators included. Any other
    /// string has to equal the URL exactly. A string without a scheme, such as <c>/dashboard</c>, is
    /// resolved against <see cref="ContextOptions.BaseURL"/> first when one is set.
    /// </param>
    /// <param name="options">The timeout in milliseconds. The other navigation options are unused.</param>
    /// <remarks>
    /// A URL that only starts with or contains the string is not a match:
    /// <c>https://example.com/</c> is not satisfied by <c>https://example.com/account</c>, and
    /// <c>https://example.com/orders?page=1</c> is not satisfied by
    /// <c>https://example.com/orders?page=10</c>. To match part of a URL, use a glob such as
    /// <c>**/orders**</c>, or one of the overloads taking a <see cref="Regex"/> or a predicate.
    /// A navigation that keeps the same document, through the history API or to a fragment,
    /// changes the URL as well and is waited for the same way.
    /// </remarks>
    /// <exception cref="WaitTimeoutException">The URL did not match within the timeout.</exception>
    Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null);

    /// <summary>
    /// Waits until this frame's URL matches a regular expression, returning at once when it already
    /// does.
    /// </summary>
    /// <param name="urlPattern">
    /// The expression the URL has to match. It is searched for anywhere in the URL, as
    /// <see cref="Regex.IsMatch(string)"/> does, so anchor it with <c>^</c> and <c>$</c> to match
    /// the whole URL.
    /// </param>
    /// <param name="options">The timeout in milliseconds. The other navigation options are unused.</param>
    /// <remarks>
    /// Navigations that keep the same document, through the history API or to a fragment, are
    /// waited for as well.
    /// </remarks>
    /// <exception cref="WaitTimeoutException">The URL did not match within the timeout.</exception>
    Task WaitForURLAsync(Regex urlPattern, NavigationOptions? options = null);

    /// <summary>
    /// Waits until this frame's URL satisfies a predicate, returning at once when it already does.
    /// </summary>
    /// <param name="predicate">
    /// Called with the full URL, and returns <see langword="true"/> for the URL being waited for.
    /// It is called for the current URL and for each URL the frame navigates to, from one thread at a
    /// time. An exception it throws ends the wait and propagates to the caller.
    /// </param>
    /// <param name="options">The timeout in milliseconds. The other navigation options are unused.</param>
    /// <remarks>
    /// Navigations that keep the same document, through the history API or to a fragment, are
    /// waited for as well.
    /// </remarks>
    /// <exception cref="WaitTimeoutException">The URL did not match within the timeout.</exception>
    Task WaitForURLAsync(Func<string, bool> predicate, NavigationOptions? options = null);

    /// <summary>
    /// Returns the accessibility tree of this frame's document.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The frame's accessibility tree, with each node carrying the backend DOM node
    /// identifier that <see cref="LocatorByBackendNodeId"/> can target.</returns>
    /// <remarks>
    /// The tree stops at this frame's document. A child frame appears as the element that hosts it
    /// and nothing more, so reading inside a child means asking that frame in turn. This is the
    /// only way to see into a frame the browser renders in its own process, whose contents the
    /// page's own tree does not contain at all.
    /// </remarks>
    Task<AccessibilitySnapshot> AccessibilitySnapshotAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a locator for the element with the given backend DOM node identifier, as reported by
    /// <see cref="AccessibilitySnapshotAsync"/>.
    /// </summary>
    /// <param name="backendNodeId">The backend DOM node identifier.</param>
    /// <returns>A locator for that element, resolved through this frame.</returns>
    /// <remarks>
    /// Backend node identifiers last only as long as the snapshot that reported them, and are
    /// resolved against the session that owns this frame, so a node read from one frame's tree must
    /// be addressed through that same frame.
    /// </remarks>
    ILocator LocatorByBackendNodeId(long backendNodeId);

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
