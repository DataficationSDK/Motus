namespace Motus.Abstractions;

/// <summary>
/// Provides methods for touchscreen input.
/// </summary>
public interface ITouchscreen
{
    /// <summary>
    /// Dispatches a tap event at the specified position.
    /// </summary>
    /// <param name="x">The x-coordinate.</param>
    /// <param name="y">The y-coordinate.</param>
    Task TapAsync(double x, double y);
}
