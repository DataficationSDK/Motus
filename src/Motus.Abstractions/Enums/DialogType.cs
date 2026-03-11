namespace Motus.Abstractions;

/// <summary>
/// Specifies the type of JavaScript dialog.
/// </summary>
public enum DialogType
{
    /// <summary>An alert dialog with a message and OK button.</summary>
    Alert,

    /// <summary>A confirm dialog with OK and Cancel buttons.</summary>
    Confirm,

    /// <summary>A prompt dialog with a text input field.</summary>
    Prompt,

    /// <summary>A dialog triggered by the beforeunload event.</summary>
    BeforeUnload
}
