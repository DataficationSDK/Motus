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
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The attribute value, or null if the attribute is not present.</returns>
    Task<string?> GetAttributeAsync(string name, CancellationToken ct = default);

    /// <summary>
    /// Returns the text content of the element.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The text content.</returns>
    Task<string?> TextContentAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the bounding box of the element, or null if the element is not visible.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The bounding box in CSS pixels.</returns>
    Task<BoundingBox?> BoundingBoxAsync(CancellationToken ct = default);
}
