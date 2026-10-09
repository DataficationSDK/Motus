using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace Motus;

/// <summary>
/// Maps key names to CDP key event properties, following a US keyboard layout.
/// </summary>
/// <remarks>
/// A key can be named by its DOM <c>code</c> ("KeyA", "Digit1", "ShiftRight", "NumpadEnter"),
/// by the <c>key</c> value it produces ("a", "A", "!", "Enter"), or by one of a few aliases
/// ("Space", "\n", "\r"). The Windows virtual key code is the one the physical key carries on a
/// US layout, so punctuation such as '.' travels as VK_OEM_PERIOD rather than a code the browser
/// would treat as a navigation or editing key.
/// </remarks>
internal static class KeyDefinitions
{
    // CDP modifier bits, as carried in the modifiers field of Input.dispatchKeyEvent.
    internal const int AltBit = 1;
    internal const int ControlBit = 2;
    internal const int MetaBit = 4;
    internal const int ShiftBit = 8;

    /// <summary>
    /// The properties sent for one key.
    /// </summary>
    /// <param name="Key">The DOM <c>key</c> value.</param>
    /// <param name="Code">The DOM <c>code</c> value of the physical key.</param>
    /// <param name="KeyCode">The Windows virtual key code.</param>
    /// <param name="Location">The DOM key location: 0 standard, 1 left, 2 right, 3 numpad.</param>
    /// <param name="Text">The text the key produces, or empty when it produces none.</param>
    /// <param name="Shifted">The definition the same key takes while Shift is held, if it differs.</param>
    internal sealed record KeyDef(string Key, string Code, int KeyCode, int Location, string Text, KeyDef? Shifted);

    private readonly record struct Entry(string Code, int KeyCode, string Key, string? ShiftKey = null, int Location = 0);

    // One row per physical key on a US layout. Rows outside the numpad come first so a key value
    // the numpad shares ("0", "/", "Enter") resolves to the main keyboard.
    private static readonly Entry[] s_layout =
    [
        new("Escape", 27, "Escape"),
        new("F1", 112, "F1"),
        new("F2", 113, "F2"),
        new("F3", 114, "F3"),
        new("F4", 115, "F4"),
        new("F5", 116, "F5"),
        new("F6", 117, "F6"),
        new("F7", 118, "F7"),
        new("F8", 119, "F8"),
        new("F9", 120, "F9"),
        new("F10", 121, "F10"),
        new("F11", 122, "F11"),
        new("F12", 123, "F12"),
        new("PrintScreen", 44, "PrintScreen"),
        new("ScrollLock", 145, "ScrollLock"),
        new("Pause", 19, "Pause"),

        new("Backquote", 192, "`", "~"),
        new("Digit1", 49, "1", "!"),
        new("Digit2", 50, "2", "@"),
        new("Digit3", 51, "3", "#"),
        new("Digit4", 52, "4", "$"),
        new("Digit5", 53, "5", "%"),
        new("Digit6", 54, "6", "^"),
        new("Digit7", 55, "7", "&"),
        new("Digit8", 56, "8", "*"),
        new("Digit9", 57, "9", "("),
        new("Digit0", 48, "0", ")"),
        new("Minus", 189, "-", "_"),
        new("Equal", 187, "=", "+"),
        new("Backspace", 8, "Backspace"),

        new("Tab", 9, "Tab"),
        new("KeyQ", 81, "q", "Q"),
        new("KeyW", 87, "w", "W"),
        new("KeyE", 69, "e", "E"),
        new("KeyR", 82, "r", "R"),
        new("KeyT", 84, "t", "T"),
        new("KeyY", 89, "y", "Y"),
        new("KeyU", 85, "u", "U"),
        new("KeyI", 73, "i", "I"),
        new("KeyO", 79, "o", "O"),
        new("KeyP", 80, "p", "P"),
        new("BracketLeft", 219, "[", "{"),
        new("BracketRight", 221, "]", "}"),
        new("Backslash", 220, "\\", "|"),

        new("CapsLock", 20, "CapsLock"),
        new("KeyA", 65, "a", "A"),
        new("KeyS", 83, "s", "S"),
        new("KeyD", 68, "d", "D"),
        new("KeyF", 70, "f", "F"),
        new("KeyG", 71, "g", "G"),
        new("KeyH", 72, "h", "H"),
        new("KeyJ", 74, "j", "J"),
        new("KeyK", 75, "k", "K"),
        new("KeyL", 76, "l", "L"),
        new("Semicolon", 186, ";", ":"),
        new("Quote", 222, "'", "\""),
        new("Enter", 13, "Enter"),

        new("ShiftLeft", 16, "Shift", Location: 1),
        new("KeyZ", 90, "z", "Z"),
        new("KeyX", 88, "x", "X"),
        new("KeyC", 67, "c", "C"),
        new("KeyV", 86, "v", "V"),
        new("KeyB", 66, "b", "B"),
        new("KeyN", 78, "n", "N"),
        new("KeyM", 77, "m", "M"),
        new("Comma", 188, ",", "<"),
        new("Period", 190, ".", ">"),
        new("Slash", 191, "/", "?"),
        new("ShiftRight", 16, "Shift", Location: 2),

        new("ControlLeft", 17, "Control", Location: 1),
        new("MetaLeft", 91, "Meta", Location: 1),
        new("AltLeft", 18, "Alt", Location: 1),
        new("Space", 32, " "),
        new("AltRight", 18, "Alt", Location: 2),
        new("MetaRight", 92, "Meta", Location: 2),
        new("ContextMenu", 93, "ContextMenu"),
        new("ControlRight", 17, "Control", Location: 2),

        new("Insert", 45, "Insert"),
        new("Home", 36, "Home"),
        new("PageUp", 33, "PageUp"),
        new("Delete", 46, "Delete"),
        new("End", 35, "End"),
        new("PageDown", 34, "PageDown"),
        new("ArrowUp", 38, "ArrowUp"),
        new("ArrowLeft", 37, "ArrowLeft"),
        new("ArrowDown", 40, "ArrowDown"),
        new("ArrowRight", 39, "ArrowRight"),

        new("AudioVolumeMute", 173, "AudioVolumeMute"),
        new("AudioVolumeDown", 174, "AudioVolumeDown"),
        new("AudioVolumeUp", 175, "AudioVolumeUp"),
        new("MediaTrackNext", 176, "MediaTrackNext"),
        new("MediaTrackPrevious", 177, "MediaTrackPrevious"),
        new("MediaStop", 178, "MediaStop"),
        new("MediaPlayPause", 179, "MediaPlayPause"),

        new("NumLock", 144, "NumLock"),
        new("NumpadDivide", 111, "/", Location: 3),
        new("NumpadMultiply", 106, "*", Location: 3),
        new("NumpadSubtract", 109, "-", Location: 3),
        new("Numpad7", 103, "7", Location: 3),
        new("Numpad8", 104, "8", Location: 3),
        new("Numpad9", 105, "9", Location: 3),
        new("Numpad4", 100, "4", Location: 3),
        new("Numpad5", 101, "5", Location: 3),
        new("Numpad6", 102, "6", Location: 3),
        new("NumpadAdd", 107, "+", Location: 3),
        new("Numpad1", 97, "1", Location: 3),
        new("Numpad2", 98, "2", Location: 3),
        new("Numpad3", 99, "3", Location: 3),
        new("Numpad0", 96, "0", Location: 3),
        new("NumpadDecimal", 110, ".", Location: 3),
        new("NumpadEnter", 13, "Enter", Location: 3),
    ];

    /// <summary>
    /// Every name a key answers to, mapped to its definition.
    /// </summary>
    internal static readonly FrozenDictionary<string, KeyDef> Keys = Build();

    private static FrozenDictionary<string, KeyDef> Build()
    {
        var byName = new Dictionary<string, KeyDef>(StringComparer.Ordinal);

        foreach (var entry in s_layout)
        {
            var shifted = entry.ShiftKey is null
                ? null
                : new KeyDef(entry.ShiftKey, entry.Code, entry.KeyCode, entry.Location, TextFor(entry.ShiftKey), null);
            var def = new KeyDef(entry.Key, entry.Code, entry.KeyCode, entry.Location, TextFor(entry.Key), shifted);

            byName[entry.Code] = def;

            // The first key to claim a value keeps it, so "Shift" means ShiftLeft and "Enter"
            // means the main Enter rather than the numpad one.
            byName.TryAdd(entry.Key, def);
            if (shifted is not null)
                byName.TryAdd(shifted.Key, shifted);
        }

        var enter = byName["Enter"];
        byName["\n"] = enter;
        byName["\r"] = enter;
        byName["Space"] = byName[" "];

        return byName.ToFrozenDictionary(StringComparer.Ordinal);
    }

    // Enter produces a carriage return, as a real keyboard does; a key whose value is a single
    // character produces that character; every other named key produces nothing.
    private static string TextFor(string key) => key switch
    {
        "Enter" => "\r",
        _ when key.Length == 1 => key,
        _ => string.Empty,
    };

    /// <summary>
    /// Resolves a key name to its definition, or returns false when the name is not a key on a
    /// US layout.
    /// </summary>
    internal static bool TryResolve(string key, [NotNullWhen(true)] out KeyDef? def)
        => Keys.TryGetValue(key, out def);

    /// <summary>
    /// Resolves a key name to its definition.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not a key on a US layout.</exception>
    internal static KeyDef Resolve(string key)
        => TryResolve(key, out var def) ? def : throw new ArgumentException($"Unknown key: '{key}'");

    /// <summary>
    /// Returns the CDP modifier bit a key sets while it is held, or 0 when it is not a modifier.
    /// </summary>
    internal static int ModifierBit(KeyDef def) => def.Key switch
    {
        "Alt" => AltBit,
        "Control" => ControlBit,
        "Meta" => MetaBit,
        "Shift" => ShiftBit,
        _ => 0,
    };
}
