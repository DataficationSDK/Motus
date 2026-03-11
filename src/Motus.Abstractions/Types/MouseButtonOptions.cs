namespace Motus.Abstractions;

/// <summary>
/// Options for mouse button operations.
/// </summary>
/// <param name="Button">The mouse button to use.</param>
/// <param name="ClickCount">Number of times to click.</param>
/// <param name="Delay">Time to wait between mouse down and mouse up in milliseconds.</param>
public sealed record MouseButtonOptions(
    MouseButton Button = MouseButton.Left,
    int? ClickCount = null,
    int? Delay = null);
