namespace Motus.Abstractions;

/// <summary>
/// Options for keyboard press operations.
/// </summary>
/// <param name="Delay">Time to wait between key down and key up in milliseconds.</param>
public sealed record KeyboardPressOptions(int? Delay = null);
