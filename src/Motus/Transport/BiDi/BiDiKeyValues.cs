using System.Collections.Frozen;
using System.Globalization;

namespace Motus;

/// <summary>
/// Maps the key of a CDP key event to the value a WebDriver BiDi key action takes.
/// </summary>
/// <remarks>
/// A WebDriver key action names its key with a single character. A key that produces a character
/// is named by that character, and every other key by the Private Use Area code point the
/// WebDriver key table assigns it: U+E006 for Enter, U+E004 for Tab, U+E008 for the left Shift
/// and U+E050 for the right one. The browser derives the event's key, code and location from that
/// code point, so the left and right modifiers, the main and numpad Enter, and the numpad digits
/// and operators each have values of their own.
/// </remarks>
internal static class BiDiKeyValues
{
    // Keys whose value depends on the physical key as well as the key it produces. The key is part
    // of the lookup because a numpad key with Num Lock off produces a navigation key instead of its
    // digit, and WebDriver gives those a separate set of code points.
    private static readonly FrozenDictionary<(string Code, string Key), string> s_byCodeAndKey =
        new Dictionary<(string Code, string Key), string>
        {
            [("ShiftLeft", "Shift")] = "",
            [("ShiftRight", "Shift")] = "",
            [("ControlLeft", "Control")] = "",
            [("ControlRight", "Control")] = "",
            [("AltLeft", "Alt")] = "",
            [("AltRight", "Alt")] = "",
            [("MetaLeft", "Meta")] = "",
            [("MetaRight", "Meta")] = "",
            [("Enter", "Enter")] = "",
            [("NumpadEnter", "Enter")] = "",

            [("NumpadEqual", "=")] = "",
            [("Numpad0", "0")] = "",
            [("Numpad1", "1")] = "",
            [("Numpad2", "2")] = "",
            [("Numpad3", "3")] = "",
            [("Numpad4", "4")] = "",
            [("Numpad5", "5")] = "",
            [("Numpad6", "6")] = "",
            [("Numpad7", "7")] = "",
            [("Numpad8", "8")] = "",
            [("Numpad9", "9")] = "",
            [("NumpadMultiply", "*")] = "",
            [("NumpadAdd", "+")] = "",
            [("NumpadComma", ",")] = "",
            [("NumpadSubtract", "-")] = "",
            [("NumpadDecimal", ".")] = "",
            [("NumpadDivide", "/")] = "",

            [("Numpad9", "PageUp")] = "",
            [("Numpad3", "PageDown")] = "",
            [("Numpad1", "End")] = "",
            [("Numpad7", "Home")] = "",
            [("Numpad4", "ArrowLeft")] = "",
            [("Numpad8", "ArrowUp")] = "",
            [("Numpad6", "ArrowRight")] = "",
            [("Numpad2", "ArrowDown")] = "",
            [("Numpad0", "Insert")] = "",
            [("NumpadDecimal", "Delete")] = "",
        }.ToFrozenDictionary();

    // Named keys by the key they produce, for an event whose code is missing or says nothing more.
    // A modifier named without its code is the left one, as it is on the keyboard.
    private static readonly FrozenDictionary<string, string> s_byKey =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Unidentified"] = "",
            ["Cancel"] = "",
            ["Help"] = "",
            ["Backspace"] = "",
            ["Tab"] = "",
            ["Clear"] = "",
            ["Enter"] = "",
            ["Shift"] = "",
            ["Control"] = "",
            ["Alt"] = "",
            ["Pause"] = "",
            ["Escape"] = "",
            ["PageUp"] = "",
            ["PageDown"] = "",
            ["End"] = "",
            ["Home"] = "",
            ["ArrowLeft"] = "",
            ["ArrowUp"] = "",
            ["ArrowRight"] = "",
            ["ArrowDown"] = "",
            ["Insert"] = "",
            ["Delete"] = "",
            ["F1"] = "",
            ["F2"] = "",
            ["F3"] = "",
            ["F4"] = "",
            ["F5"] = "",
            ["F6"] = "",
            ["F7"] = "",
            ["F8"] = "",
            ["F9"] = "",
            ["F10"] = "",
            ["F11"] = "",
            ["F12"] = "",
            ["Meta"] = "",
            ["ZenkakuHankaku"] = "",
        }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Returns the WebDriver key action value for a CDP key event's key and code.
    /// </summary>
    /// <param name="key">The DOM <c>key</c> value, or the event's text when it carries no key.</param>
    /// <param name="code">The DOM <c>code</c> value, or null when the event carries none.</param>
    /// <exception cref="ArgumentException"><paramref name="key"/> is empty.</exception>
    /// <exception cref="NotSupportedException">
    /// The key is named, but WebDriver has no value for it, as with CapsLock, NumLock or the media keys.
    /// </exception>
    internal static string FromCdp(string key, string? code)
    {
        if (key.Length == 0)
            throw new ArgumentException("A key event needs a key or text to send over WebDriver BiDi.", nameof(key));

        // Enter's text stands in for its key when an event carries text alone.
        if (key is "\r" or "\n")
            key = "Enter";

        if (code is not null && s_byCodeAndKey.TryGetValue((code, key), out var value))
            return value;

        if (s_byKey.TryGetValue(key, out value))
            return value;

        // A key that produces a character, including one off the US layout such as an accented
        // letter or an emoji, is pressed as that character.
        if (new StringInfo(key).LengthInTextElements == 1)
            return key;

        throw new NotSupportedException(
            $"The key '{key}' cannot be pressed over WebDriver BiDi (Firefox): WebDriver defines no key value for it.");
    }
}
