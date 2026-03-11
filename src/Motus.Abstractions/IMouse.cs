namespace Motus.Abstractions;

/// <summary>
/// Provides methods for mouse input.
/// </summary>
public interface IMouse
{
    /// <summary>
    /// Moves the mouse to the specified position.
    /// </summary>
    /// <param name="x">The x-coordinate.</param>
    /// <param name="y">The y-coordinate.</param>
    /// <param name="options">Move options.</param>
    Task MoveAsync(double x, double y, MouseMoveOptions? options = null);

    /// <summary>
    /// Dispatches a mouse down event.
    /// </summary>
    /// <param name="options">Button options.</param>
    Task DownAsync(MouseButtonOptions? options = null);

    /// <summary>
    /// Dispatches a mouse up event.
    /// </summary>
    /// <param name="options">Button options.</param>
    Task UpAsync(MouseButtonOptions? options = null);

    /// <summary>
    /// Clicks at the specified position.
    /// </summary>
    /// <param name="x">The x-coordinate.</param>
    /// <param name="y">The y-coordinate.</param>
    /// <param name="options">Button options.</param>
    Task ClickAsync(double x, double y, MouseButtonOptions? options = null);

    /// <summary>
    /// Double-clicks at the specified position.
    /// </summary>
    /// <param name="x">The x-coordinate.</param>
    /// <param name="y">The y-coordinate.</param>
    /// <param name="options">Button options.</param>
    Task DblClickAsync(double x, double y, MouseButtonOptions? options = null);

    /// <summary>
    /// Dispatches a mouse wheel event.
    /// </summary>
    /// <param name="deltaX">Horizontal scroll amount.</param>
    /// <param name="deltaY">Vertical scroll amount.</param>
    Task WheelAsync(double deltaX, double deltaY);
}
