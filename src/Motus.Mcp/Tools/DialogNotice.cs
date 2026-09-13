using ModelContextProtocol.Protocol;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// What a tool result says about an open JavaScript dialog. Two sentences, in one place: the one
/// the action that opened the dialog returns, and the one every later result carries until the
/// dialog is answered.
/// </summary>
/// <remarks>
/// Both name <c>handle_dialog</c>, because a dialog is the one page state an agent cannot act its
/// way out of: the browser ignores input while it is up, so every other tool it might reach for
/// would sit there until it timed out.
/// </remarks>
internal static class DialogNotice
{
    /// <summary>The result of the action that opened the dialog.</summary>
    public static string Interrupted(IDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        var name = Name(dialog.Type);
        var article = name.StartsWith('a') ? "an" : "a";
        return $"The action opened {article} {name} dialog: \"{dialog.Message}\". "
            + "Call handle_dialog to accept or dismiss it. The page finishes the action once the "
            + "dialog is answered, so a dialog opened from a mousedown handler means the click "
            + "lands after this call returned.";
    }

    /// <summary>The line every tool result carries while a dialog is waiting to be answered.</summary>
    public static string Pending(IDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return $"A \"{Name(dialog.Type)}\" dialog is open: \"{dialog.Message}\". "
            + "Handle it with handle_dialog before other actions.";
    }

    /// <summary>
    /// Puts <see cref="Pending"/> in front of a result when a dialog is waiting, so an agent that
    /// went on to do something else is told why the page is not moving.
    /// </summary>
    /// <remarks>
    /// The dialog is read without being taken, so <c>handle_dialog</c> still finds it. A result
    /// that already says the same thing is left alone: the action that opened the dialog reports
    /// it in full, and repeating it underneath would read as two dialogs.
    /// </remarks>
    public static CallToolResult Prefix(DialogService? dialogService, CallToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (dialogService?.PeekPendingDialog() is not { } dialog)
            return result;

        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text
                && text.Text.Contains("handle_dialog", StringComparison.Ordinal)
                && text.Text.Contains(dialog.Message, StringComparison.Ordinal))
                return result;
        }

        var content = new List<ContentBlock>(result.Content.Count + 1)
        {
            new TextContentBlock { Text = Pending(dialog) },
        };
        content.AddRange(result.Content);

        return new CallToolResult
        {
            Content = content,
            StructuredContent = result.StructuredContent,
            IsError = result.IsError,
        };
    }

    private static string Name(DialogType type) => type switch
    {
        DialogType.Alert => "alert",
        DialogType.Confirm => "confirm",
        DialogType.Prompt => "prompt",
        DialogType.BeforeUnload => "beforeunload",
        _ => "dialog",
    };
}
