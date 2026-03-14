using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents a JavaScript dialog (alert, confirm, prompt, or beforeunload).
/// </summary>
internal sealed class Dialog : IDialog
{
    private readonly CdpSession _session;

    internal Dialog(CdpSession session, DialogType type, string message, string? defaultValue)
    {
        _session = session;
        Type = type;
        Message = message;
        DefaultValue = defaultValue;
    }

    public DialogType Type { get; }

    public string Message { get; }

    public string? DefaultValue { get; }

    public async Task AcceptAsync(string? promptText = null)
    {
        var command = new PageHandleJavaScriptDialogParams(Accept: true, PromptText: promptText);
        await _session.SendAsync(
            "Page.handleJavaScriptDialog",
            command,
            CdpJsonContext.Default.PageHandleJavaScriptDialogParams,
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task DismissAsync()
    {
        var command = new PageHandleJavaScriptDialogParams(Accept: false);
        await _session.SendAsync(
            "Page.handleJavaScriptDialog",
            command,
            CdpJsonContext.Default.PageHandleJavaScriptDialogParams,
            CancellationToken.None).ConfigureAwait(false);
    }
}
