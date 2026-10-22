namespace Motus.Abstractions;

/// <summary>
/// Provides methods for keyboard input.
/// </summary>
/// <remarks>
/// Keys follow a US keyboard layout. A key can be named by the value it produces ("a", "A", "!",
/// "Enter", "Escape", "ArrowDown", "F5") or by the physical key's code ("KeyA", "Digit1",
/// "ShiftRight", "ControlRight", "NumpadEnter", "Numpad0"). "Space" is accepted for the space bar.
/// A name that is neither throws an <see cref="ArgumentException"/>.
/// <para>
/// Keys that produce text deliver it with the key press, so Enter activates a focused button,
/// submits a form from a text field and starts a new line in a text area, and Space toggles a
/// focused checkbox. Modifiers held with <see cref="DownAsync"/> apply to every key pressed until
/// they are released: with Shift held, "a" arrives as "A"; with Control, Alt or Meta held, a key
/// produces no text and reaches the page only as a shortcut.
/// </para>
/// </remarks>
public interface IKeyboard
{
    /// <summary>
    /// Dispatches a key down event.
    /// </summary>
    /// <param name="key">The key to press (e.g. "Shift", "a", "ArrowDown").</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not a known key name.</exception>
    Task DownAsync(string key);

    /// <summary>
    /// Dispatches a key up event.
    /// </summary>
    /// <param name="key">The key to release.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is not a known key name.</exception>
    Task UpAsync(string key);

    /// <summary>
    /// Presses a key (dispatches key down, then key up).
    /// </summary>
    /// <remarks>
    /// Keys joined with '+' are pressed together: each goes down in order and they are released
    /// in reverse. A '+' that does not follow a key name is the plus key itself, so "+" and
    /// "Shift++" both press it.
    /// </remarks>
    /// <param name="key">The key to press (e.g. "Enter", "Control+c").</param>
    /// <param name="options">Press options.</param>
    /// <exception cref="ArgumentException">A key in <paramref name="key"/> is not a known key name. Nothing is pressed.</exception>
    Task PressAsync(string key, KeyboardPressOptions? options = null);

    /// <summary>
    /// Types text character by character, dispatching key events for each character.
    /// </summary>
    /// <remarks>
    /// A character on a US keyboard is pressed as its key, so "\n" and "\r" press Enter.
    /// Any other character, including accented letters, emoji and "\t", is inserted as text
    /// the way <see cref="InsertTextAsync"/> does, without key events. To move focus with the
    /// Tab key, use <see cref="PressAsync"/> with "Tab".
    /// </remarks>
    /// <param name="text">The text to type.</param>
    /// <param name="options">Typing options.</param>
    Task TypeAsync(string text, KeyboardTypeOptions? options = null);

    /// <summary>
    /// Inserts text directly without dispatching individual key events.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    Task InsertTextAsync(string text);
}
