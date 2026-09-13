namespace Motus.Abstractions;

/// <summary>
/// Options for keyboard press operations.
/// </summary>
/// <param name="Delay">Time to wait between key down and key up in milliseconds.</param>
/// <param name="Timeout">
/// Maximum time in milliseconds to wait for the element, when pressing through a locator. Ignored
/// by <see cref="IKeyboard"/>, which dispatches the key wherever the focus already is and waits for
/// nothing.
/// </param>
public sealed record KeyboardPressOptions(int? Delay = null, double? Timeout = null);
