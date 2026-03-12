using System.Collections.Frozen;

namespace Motus;

/// <summary>
/// Maps key names to CDP key event properties.
/// </summary>
internal static class KeyDefinitions
{
    internal readonly record struct KeyDef(string Key, string Code, int KeyCode, int Location);

    internal static readonly FrozenDictionary<string, KeyDef> Keys = new Dictionary<string, KeyDef>
    {
        // Modifier keys
        ["Control"] = new("Control", "ControlLeft", 17, 1),
        ["Alt"] = new("Alt", "AltLeft", 18, 1),
        ["Shift"] = new("Shift", "ShiftLeft", 16, 1),
        ["Meta"] = new("Meta", "MetaLeft", 91, 1),

        // Navigation
        ["ArrowUp"] = new("ArrowUp", "ArrowUp", 38, 0),
        ["ArrowDown"] = new("ArrowDown", "ArrowDown", 40, 0),
        ["ArrowLeft"] = new("ArrowLeft", "ArrowLeft", 37, 0),
        ["ArrowRight"] = new("ArrowRight", "ArrowRight", 39, 0),
        ["Home"] = new("Home", "Home", 36, 0),
        ["End"] = new("End", "End", 35, 0),
        ["PageUp"] = new("PageUp", "PageUp", 33, 0),
        ["PageDown"] = new("PageDown", "PageDown", 34, 0),

        // Editing
        ["Enter"] = new("Enter", "Enter", 13, 0),
        ["Tab"] = new("Tab", "Tab", 9, 0),
        ["Backspace"] = new("Backspace", "Backspace", 8, 0),
        ["Delete"] = new("Delete", "Delete", 46, 0),
        ["Escape"] = new("Escape", "Escape", 27, 0),
        [" "] = new(" ", "Space", 32, 0),
        ["Space"] = new(" ", "Space", 32, 0),
        ["Insert"] = new("Insert", "Insert", 45, 0),

        // Function keys
        ["F1"] = new("F1", "F1", 112, 0),
        ["F2"] = new("F2", "F2", 113, 0),
        ["F3"] = new("F3", "F3", 114, 0),
        ["F4"] = new("F4", "F4", 115, 0),
        ["F5"] = new("F5", "F5", 116, 0),
        ["F6"] = new("F6", "F6", 117, 0),
        ["F7"] = new("F7", "F7", 118, 0),
        ["F8"] = new("F8", "F8", 119, 0),
        ["F9"] = new("F9", "F9", 120, 0),
        ["F10"] = new("F10", "F10", 121, 0),
        ["F11"] = new("F11", "F11", 122, 0),
        ["F12"] = new("F12", "F12", 123, 0),
    }.ToFrozenDictionary();

    /// <summary>
    /// Resolves a key name to a KeyDef. For single printable characters,
    /// generates the definition dynamically.
    /// </summary>
    internal static KeyDef Resolve(string key)
    {
        if (Keys.TryGetValue(key, out var def))
            return def;

        // Single printable character
        if (key.Length == 1)
        {
            var c = key[0];
            var upper = char.ToUpperInvariant(c);
            var code = char.IsLetter(c) ? $"Key{upper}" : $"Digit{c}";
            return new KeyDef(key, code, upper, 0);
        }

        // Unknown key, pass through as-is
        return new KeyDef(key, key, 0, 0);
    }

    /// <summary>
    /// Returns true if the key is a modifier (Control, Alt, Shift, Meta).
    /// </summary>
    internal static bool IsModifier(string key) =>
        key is "Control" or "Alt" or "Shift" or "Meta";
}
