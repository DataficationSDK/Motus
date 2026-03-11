namespace Motus.Abstractions;

/// <summary>
/// Options for creating a locator.
/// </summary>
public sealed record LocatorOptions
{
    /// <summary>Matches elements containing specified text somewhere inside, possibly in a child element.</summary>
    public string? HasText { get; init; }

    /// <summary>Matches elements that have a descendant matching the inner locator.</summary>
    public ILocator? Has { get; init; }

    /// <summary>Matches elements that do not have a descendant matching the inner locator.</summary>
    public ILocator? HasNot { get; init; }

    /// <summary>Maximum time in milliseconds to wait for the element.</summary>
    public double? Timeout { get; init; }
}
