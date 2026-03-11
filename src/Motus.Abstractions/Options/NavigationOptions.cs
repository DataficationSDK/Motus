namespace Motus.Abstractions;

/// <summary>
/// Options for page navigation operations.
/// </summary>
public sealed record NavigationOptions
{
    /// <summary>When to consider the navigation complete.</summary>
    public WaitUntil? WaitUntil { get; init; }

    /// <summary>Maximum time in milliseconds to wait for the navigation.</summary>
    public double? Timeout { get; init; }

    /// <summary>Referer header to use for the navigation request.</summary>
    public string? Referer { get; init; }
}
