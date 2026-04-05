namespace Motus.Abstractions;

/// <summary>
/// Pre-fetched computed style properties for an element, used by accessibility rules
/// that need CSS information (e.g., color contrast checks).
/// </summary>
public sealed record ComputedStyleInfo(
    string? Color,
    string? BackgroundColor,
    string? FontSize,
    string? FontWeight);
