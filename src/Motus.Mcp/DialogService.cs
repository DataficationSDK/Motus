using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Captures the JavaScript dialog (alert, confirm, prompt, or beforeunload) that a
/// page raises, so a later tool call can accept or dismiss it. A dialog blocks the
/// page until it is answered, and tool calls arrive as individually stateless
/// messages, so the pending dialog has to be held here between the call that
/// triggers it and the call that handles it.
/// </summary>
/// <remarks>
/// The subscription follows the active page: <see cref="Subscribe"/> is called each
/// time the active page is resolved or switched, detaching from the previous page
/// first. Under <see cref="DialogPolicy.Ask"/> a dialog that arrives with no handler is
/// left pending rather than auto-answered, so the agent decides the outcome. If a second
/// dialog arrives before the first is handled, the later one wins.
/// <para>
/// A dialog also has to interrupt whatever opened it. The browser stops answering input while a
/// dialog is up, so the command that dispatched the click sits unanswered until the transport
/// gives up on it a minute later, and the tool call that would have reported the dialog is the
/// very call that is stuck behind it. <see cref="ArmAsync"/> gives a caller something to race the
/// action against, so the dialog is reported the moment it opens.
/// </para>
/// </remarks>
public sealed class DialogService
{
    private IPage? _subscribedPage;
    private IDialog? _pendingDialog;
    private TaskCompletionSource<IDialog>? _armed;

    /// <summary>
    /// What to do with a dialog when it opens. Defaults to <see cref="DialogPolicy.Ask"/>, which
    /// holds it for <c>handle_dialog</c>; the other two answer it in the event handler, so the
    /// page unblocks on its own and the action that opened it completes normally.
    /// </summary>
    public DialogPolicy Policy { get; set; } = DialogPolicy.Ask;

    /// <summary>Starts under <see cref="DialogPolicy.Ask"/>, so every dialog waits for the agent.</summary>
    public DialogService()
    {
    }

    /// <summary>Starts under the policy the server was launched with.</summary>
    public DialogService(McpServerLaunchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Policy = ParsePolicy(options.Dialogs);
    }

    /// <summary>
    /// Reads a policy name as the command line spells it. Anything unrecognized, including nothing
    /// at all, is <see cref="DialogPolicy.Ask"/>, because holding a dialog for the agent is the
    /// only choice that loses no information.
    /// </summary>
    internal static DialogPolicy ParsePolicy(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "accept" => DialogPolicy.Accept,
        "dismiss" => DialogPolicy.Dismiss,
        _ => DialogPolicy.Ask,
    };

    /// <summary>
    /// Attaches to the given page's dialog event, detaching from any previously
    /// subscribed page. A repeat call for the same page is a no-op.
    /// </summary>
    public void Subscribe(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (ReferenceEquals(_subscribedPage, page))
            return;

        if (_subscribedPage is not null)
            _subscribedPage.Dialog -= OnDialog;

        _subscribedPage = page;
        page.Dialog += OnDialog;
    }

    /// <summary>
    /// Returns a task that completes with the next dialog to open, for a caller that wants to run
    /// an action and find out immediately if the action opened one. A dialog that is already
    /// pending completes the task at once, since the page is blocked before the action even starts.
    /// </summary>
    /// <remarks>
    /// Only the most recent arming is live: a second call replaces the first, and the replaced task
    /// never completes. Nothing waits on it alone, so that is a task the garbage collector reclaims
    /// rather than a leak. Under a policy other than <see cref="DialogPolicy.Ask"/> the task never
    /// completes at all, because the dialog is answered in the handler and the action it
    /// interrupted carries on by itself.
    /// </remarks>
    public Task<IDialog> ArmAsync()
    {
        var armed = new TaskCompletionSource<IDialog>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref _armed, armed);

        // A dialog left over from an earlier call blocks this one before it starts, so report it
        // rather than let the caller wait for a second dialog that will never come.
        if (Policy == DialogPolicy.Ask && Volatile.Read(ref _pendingDialog) is { } pending)
            Complete(pending);

        return armed.Task;
    }

    /// <summary>
    /// Drops the armed task, so a dialog that opens later does not complete it. Called once the
    /// action that armed it has finished, whatever its outcome.
    /// </summary>
    public void Disarm() => Interlocked.Exchange(ref _armed, null);

    /// <summary>
    /// Returns the pending dialog and clears it, or null when none is open. The
    /// handler runs on a browser thread, so the read and clear are a single atomic
    /// exchange.
    /// </summary>
    public IDialog? TakePendingDialog() => Interlocked.Exchange(ref _pendingDialog, null);

    /// <summary>
    /// Returns the pending dialog without clearing it, or null when none is open. This is the read
    /// for anything that only wants to say a dialog is open; <see cref="TakePendingDialog"/> stays
    /// the read for whatever is about to answer it.
    /// </summary>
    public IDialog? PeekPendingDialog() => Volatile.Read(ref _pendingDialog);

    private void OnDialog(object? sender, DialogEventArgs e)
    {
        var policy = Policy;
        if (policy is DialogPolicy.Accept or DialogPolicy.Dismiss)
        {
            // Answer it here rather than hold it. The page is blocked until the answer reaches the
            // browser, so the sooner the better, and nothing later in the session is going to want
            // a dialog whose answer was decided at launch.
            _ = AnswerAsync(e.Dialog, accept: policy == DialogPolicy.Accept);
            return;
        }

        Interlocked.Exchange(ref _pendingDialog, e.Dialog);
        Complete(e.Dialog);
    }

    private void Complete(IDialog dialog) => Interlocked.Exchange(ref _armed, null)?.TrySetResult(dialog);

    private static async Task AnswerAsync(IDialog dialog, bool accept)
    {
        try
        {
            if (accept)
                await dialog.AcceptAsync().ConfigureAwait(false);
            else
                await dialog.DismissAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Nothing is waiting on this and there is nobody to tell. A dialog that could not be
            // answered leaves the page blocked, which the next tool call reports on its own.
        }
    }
}
