namespace Motus.Abstractions;

/// <summary>
/// Options for assertions on pages, locators, and other objects.
/// </summary>
public sealed record AssertionOptions
{
    /// <summary>Maximum time in milliseconds to wait for the assertion to pass.</summary>
    public int? Timeout { get; init; }

    /// <summary>Custom error message to display on assertion failure.</summary>
    public string? Message { get; init; }
}
