namespace Motus.Abstractions;

/// <summary>
/// Represents the viewport dimensions.
/// </summary>
/// <param name="Width">The viewport width in pixels.</param>
/// <param name="Height">The viewport height in pixels.</param>
public sealed record ViewportSize(int Width, int Height);
