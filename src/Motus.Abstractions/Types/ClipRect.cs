namespace Motus.Abstractions;

/// <summary>
/// Specifies the clipping region for a screenshot.
/// </summary>
/// <param name="X">The x-coordinate of the top-left corner.</param>
/// <param name="Y">The y-coordinate of the top-left corner.</param>
/// <param name="Width">The width of the clipping region.</param>
/// <param name="Height">The height of the clipping region.</param>
public sealed record ClipRect(double X, double Y, double Width, double Height);
