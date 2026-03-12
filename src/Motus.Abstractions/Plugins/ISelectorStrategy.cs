namespace Motus.Abstractions;

/// <summary>
/// Defines a custom element-targeting strategy beyond the built-in CSS, XPath, and text selectors.
/// </summary>
public interface ISelectorStrategy
{
    /// <summary>
    /// Gets the prefix used in selector strings (e.g. "data-test" for data-test=login-btn).
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// Gets the precedence when multiple strategies can resolve the same selector.
    /// Higher priority wins. Used by the recorder's selector inference to rank candidates.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Resolves the selector expression to a list of matching elements within the given frame.
    /// </summary>
    /// <param name="selector">The selector expression to resolve.</param>
    /// <param name="frame">The frame to search within.</param>
    /// <param name="pierceShadow">Whether to descend into open shadow roots when resolving.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A list of matching elements.</returns>
    Task<IReadOnlyList<IElementHandle>> ResolveAsync(string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default);

    /// <summary>
    /// Given an element handle, generates the best selector for it using this strategy.
    /// Returns null if this strategy cannot produce a selector for the element.
    /// </summary>
    /// <param name="element">The element to generate a selector for.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The generated selector, or null.</returns>
    Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default);
}
