namespace Motus.Abstractions;

/// <summary>
/// Represents the bounding box of an element in CSS pixels.
/// </summary>
/// <param name="X">The x-coordinate of the top-left corner.</param>
/// <param name="Y">The y-coordinate of the top-left corner.</param>
/// <param name="Width">The width of the bounding box.</param>
/// <param name="Height">The height of the bounding box.</param>
public sealed record BoundingBox(double X, double Y, double Width, double Height);
