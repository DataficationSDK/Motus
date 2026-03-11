namespace Motus.Abstractions;

/// <summary>
/// Options for page navigation operations.
/// </summary>
public sealed record NavigationOptions
{
    /// <summary>When to consider the navigation complete.</summary>
    public WaitUntil WaitUntil { get; init; } = WaitUntil.Load;

    /// <summary>Maximum time in milliseconds to wait for the navigation.</summary>
    public int? Timeout { get; init; }

    /// <summary>Referer header to use for the navigation request.</summary>
    public string? Referer { get; init; }
}
