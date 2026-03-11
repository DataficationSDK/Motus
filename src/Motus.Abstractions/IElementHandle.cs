namespace Motus.Abstractions;

/// <summary>
/// Represents a handle to a DOM element in the page. Prefer <see cref="ILocator"/> for most use cases.
/// </summary>
public interface IElementHandle : IJSHandle
{
    /// <summary>
    /// Gets the value of the specified attribute.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The attribute value, or null if the attribute is not present.</returns>
    Task<string?> GetAttributeAsync(string name);

    /// <summary>
    /// Returns the text content of the element.
    /// </summary>
    /// <returns>The text content.</returns>
    Task<string?> TextContentAsync();

    /// <summary>
    /// Returns the inner text of the element.
    /// </summary>
    /// <returns>The inner text.</returns>
    Task<string> InnerTextAsync();

    /// <summary>
    /// Returns the inner HTML of the element.
    /// </summary>
    /// <returns>The inner HTML markup.</returns>
    Task<string> InnerHTMLAsync();

    /// <summary>
    /// Returns the bounding box of the element, or null if the element is not visible.
    /// </summary>
    /// <returns>The bounding box in CSS pixels.</returns>
    Task<BoundingBox?> BoundingBoxAsync();

    /// <summary>
    /// Takes a screenshot of the element.
    /// </summary>
    /// <param name="options">Screenshot options.</param>
    /// <returns>The screenshot as a byte array.</returns>
    Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null);

    /// <summary>
    /// Clicks the element.
    /// </summary>
    Task ClickAsync();

    /// <summary>
    /// Double-clicks the element.
    /// </summary>
    Task DblClickAsync();

    /// <summary>
    /// Checks the element (checkbox or radio button).
    /// </summary>
    Task CheckAsync();

    /// <summary>
    /// Unchecks the element (checkbox).
    /// </summary>
    Task UncheckAsync();

    /// <summary>
    /// Fills the element with the specified text.
    /// </summary>
    /// <param name="value">The text to fill.</param>
    Task FillAsync(string value);

    /// <summary>
    /// Focuses the element.
    /// </summary>
    Task FocusAsync();

    /// <summary>
    /// Hovers over the element.
    /// </summary>
    Task HoverAsync();

    /// <summary>
    /// Selects one or more options in a select element by value.
    /// </summary>
    /// <param name="values">The option values to select.</param>
    /// <returns>The selected option values.</returns>
    Task<IReadOnlyList<string>> SelectOptionAsync(params string[] values);

    /// <summary>
    /// Types text into the element character by character.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="options">Typing options.</param>
    Task TypeAsync(string text, KeyboardTypeOptions? options = null);

    /// <summary>
    /// Presses a key on the element.
    /// </summary>
    /// <param name="key">The key to press (e.g. "Enter", "ArrowDown").</param>
    /// <param name="options">Press options.</param>
    Task PressAsync(string key, KeyboardPressOptions? options = null);

    /// <summary>
    /// Sets the files for a file input element.
    /// </summary>
    /// <param name="files">The files to set.</param>
    Task SetInputFilesAsync(IEnumerable<FilePayload> files);

    /// <summary>
    /// Waits for the element to satisfy the specified state.
    /// </summary>
    /// <param name="state">The desired element state.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    Task WaitForElementStateAsync(ElementState state, double? timeout = null);

    /// <summary>
    /// Returns whether the element is checked.
    /// </summary>
    /// <returns>True if the element is checked.</returns>
    Task<bool> IsCheckedAsync();

    /// <summary>
    /// Returns whether the element is disabled.
    /// </summary>
    /// <returns>True if the element is disabled.</returns>
    Task<bool> IsDisabledAsync();

    /// <summary>
    /// Returns whether the element is editable.
    /// </summary>
    /// <returns>True if the element is editable.</returns>
    Task<bool> IsEditableAsync();

    /// <summary>
    /// Returns whether the element is enabled.
    /// </summary>
    /// <returns>True if the element is enabled.</returns>
    Task<bool> IsEnabledAsync();

    /// <summary>
    /// Returns whether the element is hidden.
    /// </summary>
    /// <returns>True if the element is hidden.</returns>
    Task<bool> IsHiddenAsync();

    /// <summary>
    /// Returns whether the element is visible.
    /// </summary>
    /// <returns>True if the element is visible.</returns>
    Task<bool> IsVisibleAsync();
}
