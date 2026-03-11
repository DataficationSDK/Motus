namespace Motus.Abstractions;

/// <summary>
/// Options for keyboard type operations.
/// </summary>
/// <param name="Delay">Time to wait between key presses in milliseconds.</param>
public sealed record KeyboardTypeOptions(int? Delay = null);
