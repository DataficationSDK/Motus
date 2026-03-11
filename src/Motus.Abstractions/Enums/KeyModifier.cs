namespace Motus.Abstractions;

/// <summary>
/// Modifier keys that can be held during keyboard and mouse actions.
/// </summary>
[Flags]
public enum KeyModifier
{
    /// <summary>No modifier key.</summary>
    None = 0,

    /// <summary>The Alt key (Option on macOS).</summary>
    Alt = 1,

    /// <summary>The Control key.</summary>
    Control = 2,

    /// <summary>The Meta key (Command on macOS, Windows key on Windows).</summary>
    Meta = 4,

    /// <summary>The Shift key.</summary>
    Shift = 8
}
