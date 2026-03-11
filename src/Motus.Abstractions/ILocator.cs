namespace Motus.Abstractions;

/// <summary>
/// Represents a way to find elements on a page. Locators are strict by default and will throw if multiple elements match.
/// </summary>
public interface ILocator
{
    // --- Actions ---

    /// <summary>
    /// Clicks the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task ClickAsync(double? timeout = null);

    /// <summary>
    /// Double-clicks the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task DblClickAsync(double? timeout = null);

    /// <summary>
    /// Checks the element (checkbox or radio button).
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task CheckAsync(double? timeout = null);

    /// <summary>
    /// Unchecks the element (checkbox).
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task UncheckAsync(double? timeout = null);

    /// <summary>
    /// Sets the checked state of a checkbox or radio button.
    /// </summary>
    /// <param name="checked">Whether to check or uncheck the element.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task SetCheckedAsync(bool @checked, double? timeout = null);

    /// <summary>
    /// Fills the element with the specified text. Clears existing content first.
    /// </summary>
    /// <param name="value">The text to fill.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task FillAsync(string value, double? timeout = null);

    /// <summary>
    /// Clears the input field.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task ClearAsync(double? timeout = null);

    /// <summary>
    /// Types text character by character into the element.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="options">Typing options.</param>
    Task TypeAsync(string text, KeyboardTypeOptions? options = null);

    /// <summary>
    /// Presses a key on the element.
    /// </summary>
    /// <param name="key">The key to press.</param>
    /// <param name="options">Press options.</param>
    Task PressAsync(string key, KeyboardPressOptions? options = null);

    /// <summary>
    /// Focuses the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task FocusAsync(double? timeout = null);

    /// <summary>
    /// Hovers over the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task HoverAsync(double? timeout = null);

    /// <summary>
    /// Selects options in a select element by value.
    /// </summary>
    /// <param name="values">The option values to select.</param>
    /// <returns>The selected option values.</returns>
    Task<IReadOnlyList<string>> SelectOptionAsync(params string[] values);

    /// <summary>
    /// Sets the files for a file input element.
    /// </summary>
    /// <param name="files">The files to set.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task SetInputFilesAsync(IEnumerable<FilePayload> files, double? timeout = null);

    /// <summary>
    /// Taps the element (for touch-enabled devices).
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task TapAsync(double? timeout = null);

    /// <summary>
    /// Scrolls the element into view.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task ScrollIntoViewIfNeededAsync(double? timeout = null);

    /// <summary>
    /// Takes a screenshot of the element.
    /// </summary>
    /// <param name="options">Screenshot options.</param>
    /// <returns>The screenshot as a byte array.</returns>
    Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null);

    /// <summary>
    /// Dispatches an event on the element.
    /// </summary>
    /// <param name="type">The event type (e.g. "click").</param>
    /// <param name="eventInit">Optional event initialization properties.</param>
    Task DispatchEventAsync(string type, object? eventInit = null);

    /// <summary>
    /// Evaluates a JavaScript expression in the context of the matching element.
    /// </summary>
    /// <typeparam name="T">The expected return type.</typeparam>
    /// <param name="expression">The JavaScript expression. The element is available as the first argument.</param>
    /// <param name="arg">Optional argument to pass to the expression.</param>
    /// <returns>The result of the evaluation.</returns>
    Task<T> EvaluateAsync<T>(string expression, object? arg = null);

    // --- Queries ---

    /// <summary>
    /// Returns the text content of the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The text content.</returns>
    Task<string?> TextContentAsync(double? timeout = null);

    /// <summary>
    /// Returns the inner text of the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The inner text.</returns>
    Task<string> InnerTextAsync(double? timeout = null);

    /// <summary>
    /// Returns the inner HTML of the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The inner HTML markup.</returns>
    Task<string> InnerHTMLAsync(double? timeout = null);

    /// <summary>
    /// Returns the value of the specified attribute.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The attribute value, or null.</returns>
    Task<string?> GetAttributeAsync(string name, double? timeout = null);

    /// <summary>
    /// Returns the input value of the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The input value.</returns>
    Task<string> InputValueAsync(double? timeout = null);

    /// <summary>
    /// Returns the bounding box of the element.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The bounding box, or null if the element is not visible.</returns>
    Task<BoundingBox?> BoundingBoxAsync(double? timeout = null);

    /// <summary>
    /// Returns the number of elements matching the locator.
    /// </summary>
    /// <returns>The count of matching elements.</returns>
    Task<int> CountAsync();

    /// <summary>
    /// Returns all inner texts of matching elements.
    /// </summary>
    /// <returns>A list of inner text values.</returns>
    Task<IReadOnlyList<string>> AllInnerTextsAsync();

    /// <summary>
    /// Returns all text contents of matching elements.
    /// </summary>
    /// <returns>A list of text content values.</returns>
    Task<IReadOnlyList<string>> AllTextContentsAsync();

    // --- State queries ---

    /// <summary>
    /// Returns whether the element is checked.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>True if the element is checked.</returns>
    Task<bool> IsCheckedAsync(double? timeout = null);

    /// <summary>
    /// Returns whether the element is disabled.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>True if the element is disabled.</returns>
    Task<bool> IsDisabledAsync(double? timeout = null);

    /// <summary>
    /// Returns whether the element is editable.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>True if the element is editable.</returns>
    Task<bool> IsEditableAsync(double? timeout = null);

    /// <summary>
    /// Returns whether the element is enabled.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>True if the element is enabled.</returns>
    Task<bool> IsEnabledAsync(double? timeout = null);

    /// <summary>
    /// Returns whether the element is hidden.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>True if the element is hidden.</returns>
    Task<bool> IsHiddenAsync(double? timeout = null);

    /// <summary>
    /// Returns whether the element is visible.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>True if the element is visible.</returns>
    Task<bool> IsVisibleAsync(double? timeout = null);

    // --- Filtering and chaining ---

    /// <summary>
    /// Returns a locator for the first matching element.
    /// </summary>
    ILocator First { get; }

    /// <summary>
    /// Returns a locator for the last matching element.
    /// </summary>
    ILocator Last { get; }

    /// <summary>
    /// Returns a locator for the nth matching element (zero-based).
    /// </summary>
    /// <param name="index">The zero-based index.</param>
    /// <returns>A locator for the nth element.</returns>
    ILocator Nth(int index);

    /// <summary>
    /// Filters the locator to match only elements satisfying the specified options.
    /// </summary>
    /// <param name="options">Filter options.</param>
    /// <returns>A filtered locator.</returns>
    ILocator Filter(LocatorOptions? options = null);

    /// <summary>
    /// Creates a child locator scoped to the current locator.
    /// </summary>
    /// <param name="selector">The selector for child elements.</param>
    /// <param name="options">Locator options.</param>
    /// <returns>A child locator.</returns>
    ILocator Locator(string selector, LocatorOptions? options = null);

    // --- Waiting ---

    /// <summary>
    /// Waits for the element to satisfy the specified state.
    /// </summary>
    /// <param name="state">The desired element state.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task WaitForAsync(ElementState? state = null, double? timeout = null);

    // --- Element handle ---

    /// <summary>
    /// Resolves the locator to the first matching DOM element handle.
    /// </summary>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>The element handle.</returns>
    Task<IElementHandle> ElementHandleAsync(double? timeout = null);

    /// <summary>
    /// Resolves the locator to all matching DOM element handles.
    /// </summary>
    /// <returns>A list of element handles.</returns>
    Task<IReadOnlyList<IElementHandle>> ElementHandlesAsync();
}
