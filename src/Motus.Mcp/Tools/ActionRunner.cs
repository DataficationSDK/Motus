using System.Diagnostics;
using ModelContextProtocol.Protocol;

namespace Motus.Mcp;

/// <summary>
/// Runs the browser action behind a tool call and decides what the call returns. One place for the
/// concerns that belong to every action rather than to any one tool.
/// </summary>
/// <remarks>
/// The action is taken as a delegate over a cancellation token, not as a finished task, so the
/// runner owns the token the action runs under and can cut the action short. Today that is used
/// for one thing: a dialog that opens mid-action ends the call immediately instead of leaving it
/// waiting on a page the browser has stopped answering for.
/// </remarks>
internal static class ActionRunner
{
    /// <summary>
    /// Runs <paramref name="action"/> against the dialog event, and returns whichever happens
    /// first: the action's own result, or a report that the action opened a dialog. An action
    /// asked for while a dialog is already open is refused rather than run.
    /// </summary>
    /// <param name="dialogService">
    /// The dialog watcher for the session, or null when there is none (the tool unit tests), in
    /// which case the action simply runs.
    /// </param>
    /// <param name="cancellationToken">The tool call's token. The action's token is linked to it.</param>
    /// <param name="action">The action, returning the result the tool reports when it wins.</param>
    /// <remarks>
    /// Cancelling the action's token stops everything downstream that takes a token. It does not
    /// reach the input command already sitting in the browser: that one is answered the moment the
    /// dialog is, which is the very next call an agent makes, and gives up on its own if it is not.
    /// What matters is that the tool call itself no longer waits for it. The abandoned task is
    /// still awaited in the background so its eventual failure is observed rather than raised at
    /// some unrelated point later.
    /// </remarks>
    public static async Task<CallToolResult> RunAsync(
        DialogService? dialogService,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<CallToolResult>> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // A dialog already waiting blocks the page before this action starts, so dispatching into
        // it would only hang. Say so instead, and leave the dialog for handle_dialog.
        if (dialogService?.PeekPendingDialog() is { } waiting)
            return ToolResultHelper.Error(DialogNotice.Pending(waiting));

        var startedAt = Stopwatch.GetTimestamp();

        if (dialogService is null)
        {
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
            {
                return TimedOut(startedAt);
            }
        }

        var dialogOpened = dialogService.ArmAsync();
        var actionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<CallToolResult> running;
        try
        {
            running = action(actionCts.Token);
        }
        catch (Exception)
        {
            dialogService.Disarm();
            actionCts.Dispose();
            throw;
        }

        var first = await Task.WhenAny(running, dialogOpened).ConfigureAwait(false);
        if (first == running)
        {
            dialogService.Disarm();
            try
            {
                return await running.ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
            {
                return TimedOut(startedAt);
            }
            finally
            {
                actionCts.Dispose();
            }
        }

        await actionCts.CancelAsync().ConfigureAwait(false);
        Abandon(running, actionCts);

        return ToolResultHelper.Text(DialogNotice.Interrupted(await dialogOpened.ConfigureAwait(false)));
    }

    /// <summary>
    /// The message for a cancellation that reached the tool. The engine's own timeouts say which
    /// check they gave up on; a bare cancellation comes from the transport waiting on a browser
    /// that never answered, which has no step to name, so say that plainly instead of reporting
    /// "A task was canceled."
    /// </summary>
    private static CallToolResult TimedOut(long startedAt)
    {
        var seconds = Math.Max(1, (int)Math.Round(Stopwatch.GetElapsedTime(startedAt).TotalSeconds));
        return ToolResultHelper.Error(
            $"Timed out after {seconds} s waiting for the browser to answer. The page did not respond to the "
            + "action: take a snapshot to see its state, and call handle_dialog if a dialog is open.");
    }

    /// <summary>
    /// Lets a cancelled action finish in its own time, observing whatever it ends with so it is
    /// never raised as an unobserved fault, and disposing the token source once nothing holds it.
    /// </summary>
    private static void Abandon(Task task, CancellationTokenSource cts)
    {
        _ = task.ContinueWith(
            static (finished, state) =>
            {
                _ = finished.Exception;
                ((CancellationTokenSource)state!).Dispose();
            },
            cts,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
