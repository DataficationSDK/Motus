namespace Motus.Abstractions;

/// <summary>
/// Represents a JavaScript dialog (alert, confirm, prompt, or beforeunload).
/// </summary>
public interface IDialog
{
    /// <summary>
    /// Gets the type of the dialog.
    /// </summary>
    DialogType Type { get; }

    /// <summary>
    /// Gets the message displayed in the dialog.
    /// </summary>
    string Message { get; }

    /// <summary>
    /// Gets the default value of the prompt, or an empty string for non-prompt dialogs.
    /// </summary>
    string? DefaultValue { get; }

    /// <summary>
    /// Accepts the dialog, optionally providing a prompt text.
    /// </summary>
    /// <param name="promptText">The text to enter in a prompt dialog.</param>
    Task AcceptAsync(string? promptText = null);

    /// <summary>
    /// Dismisses the dialog.
    /// </summary>
    Task DismissAsync();
}
