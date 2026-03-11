namespace Motus.Abstractions;

/// <summary>
/// Provides methods for keyboard input.
/// </summary>
public interface IKeyboard
{
    /// <summary>
    /// Dispatches a key down event.
    /// </summary>
    /// <param name="key">The key to press (e.g. "Shift", "a", "ArrowDown").</param>
    Task DownAsync(string key);

    /// <summary>
    /// Dispatches a key up event.
    /// </summary>
    /// <param name="key">The key to release.</param>
    Task UpAsync(string key);

    /// <summary>
    /// Presses a key (dispatches key down, then key up).
    /// </summary>
    /// <param name="key">The key to press (e.g. "Enter", "Control+c").</param>
    /// <param name="options">Press options.</param>
    Task PressAsync(string key, KeyboardPressOptions? options = null);

    /// <summary>
    /// Types text character by character, dispatching key events for each character.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="options">Typing options.</param>
    Task TypeAsync(string text, KeyboardTypeOptions? options = null);

    /// <summary>
    /// Inserts text directly without dispatching individual key events.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    Task InsertTextAsync(string text);
}
