namespace Motus.Abstractions;

/// <summary>
/// Options for keyboard type operations.
/// </summary>
/// <param name="Delay">Time to wait between key presses in milliseconds.</param>
/// <param name="Timeout">
/// Maximum time in milliseconds to wait for the element, when typing through a locator. Ignored by
/// <see cref="IKeyboard"/>, which types wherever the focus already is and waits for nothing.
/// </param>
public sealed record KeyboardTypeOptions(int? Delay = null, double? Timeout = null);
