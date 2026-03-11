namespace Motus.Abstractions;

/// <summary>
/// Defines a custom strategy for resolving and generating element selectors.
/// </summary>
public interface ISelectorStrategy
{
    /// <summary>
    /// Gets the name of this selector strategy (e.g. "data-qa", "aria").
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// Gets the priority of this strategy. Lower values are tried first.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Resolves a selector string to matching elements on the page.
    /// </summary>
    /// <param name="page">The page to search.</param>
    /// <param name="selector">The selector string to resolve.</param>
    /// <returns>Element handles matching the selector.</returns>
    Task<IReadOnlyList<IElementHandle>> ResolveAsync(IPage page, string selector);

    /// <summary>
    /// Generates a selector string for the given element.
    /// </summary>
    /// <param name="element">The element to generate a selector for.</param>
    /// <returns>A selector string, or null if this strategy cannot generate one for the element.</returns>
    Task<string?> GenerateSelector(IElementHandle element);
}
