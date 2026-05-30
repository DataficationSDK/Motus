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
/// first. A dialog that arrives with no handler is left pending rather than
/// auto-answered, so the agent decides the outcome. If a second dialog arrives
/// before the first is handled, the later one wins.
/// </remarks>
public sealed class DialogService
{
    private IPage? _subscribedPage;
    private IDialog? _pendingDialog;

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
    /// Returns the pending dialog and clears it, or null when none is open. The
    /// handler runs on a browser thread, so the read and clear are a single atomic
    /// exchange.
    /// </summary>
    public IDialog? TakePendingDialog() => Interlocked.Exchange(ref _pendingDialog, null);

    private void OnDialog(object? sender, DialogEventArgs e)
        => Interlocked.Exchange(ref _pendingDialog, e.Dialog);
}
