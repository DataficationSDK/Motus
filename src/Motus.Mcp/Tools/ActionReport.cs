using System.Diagnostics;
using System.Text;
using ModelContextProtocol.Protocol;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// What an action changed about the session, gathered around the action and printed under the line
/// the tool returns.
/// </summary>
/// <remarks>
/// An agent told only "Clicked e7" has to spend a snapshot, a console read and a tab listing to
/// find out whether the click did anything, and those three calls cost more than the click did.
/// The few facts that change what it should do next are cheap to collect here, where the state
/// before and the state after are both to hand: where the page ended up, a tab the page opened,
/// errors the action logged, and whether the refs it is holding still mean anything. A row is
/// printed only when it applies, so a click on a quiet page is still one line.
/// <para>
/// Nothing here may reach the browser while a dialog is open. The browser stops answering while
/// one is up, so a title read would sit there for the length of the transport's timeout, which is
/// the very failure the dialog work exists to prevent. Everything used in that case is either
/// already in memory or a local property.
/// </para>
/// </remarks>
internal sealed class ActionReport : IDisposable
{
    /// <summary>
    /// How long to give the browser, after the action, to show what the action did. Read from the
    /// session's settle time (<c>--settle</c>), 500 ms unless configured.
    /// </summary>
    /// <remarks>
    /// An action returns when the browser acknowledges the input, which is before the browser has
    /// finished acting on it. A tab the click opened is announced during the click, but Motus then
    /// attaches to it and applies the context's settings before it is a tab the session can
    /// describe, and a link the click followed has not committed its navigation yet, so the page
    /// would be described as the one the agent has just left. Every action pays this, so it is
    /// short, and the wait ends as soon as a tab appears. Zero means the page is described as it
    /// stands the moment the action returns.
    /// </remarks>
    private TimeSpan SettleWait => _pageService.Settle;

    /// <summary>
    /// How long to then wait for a new tab to report the address it was opened for. A tab exists
    /// before its document does, so the first URL it offers is usually <c>about:blank</c>, which
    /// tells an agent nothing about where the tab went.
    /// </summary>
    private static readonly TimeSpan NewTabUrlWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How often the waits above look again. The delay itself is never cancelled, so that a token
    /// cancelled mid-report costs at most one more turn of the loop rather than raising out of a
    /// call whose action has already succeeded; the loops check the token themselves.
    /// </summary>
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(25);

    private readonly ActivePageService _pageService;
    private readonly IPage _page;
    private readonly List<IPage> _popups = [];
    private readonly string _url;
    private readonly string? _title;
    private readonly long _consoleCursor;
    private readonly bool _hadSnapshot;
    private readonly IReadOnlyList<IPage>? _tabsBefore;

    private bool _subscribed;

    private ActionReport(
        ActivePageService pageService,
        IPage page,
        string url,
        string? title,
        long consoleCursor,
        bool hadSnapshot,
        IReadOnlyList<IPage>? tabsBefore)
    {
        _pageService = pageService;
        _page = page;
        _url = url;
        _title = title;
        _consoleCursor = consoleCursor;
        _hadSnapshot = hadSnapshot;
        _tabsBefore = tabsBefore;

        _page.Popup += OnPopup;
        _subscribed = true;
    }

    /// <summary>
    /// Records what the session looks like before the action runs.
    /// </summary>
    public static async Task<ActionReport> BeginAsync(
        ActivePageService pageService, IPage page, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(pageService);
        ArgumentNullException.ThrowIfNull(page);

        var blocked = pageService.Dialogs?.PeekPendingDialog() is not null;

        return new ActionReport(
            pageService,
            page,
            page.Url,
            blocked ? null : await PageDescription.TryTitleAsync(page).ConfigureAwait(false),
            pageService.ConsoleLog?.NextSequence ?? 0,
            pageService.HasSnapshot(page),
            await TryListTabsAsync(pageService, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Returns the action's result with the report added under it, and a fresh snapshot after that
    /// when one was asked for.
    /// </summary>
    /// <param name="result">The result the action produced.</param>
    /// <param name="withSnapshot">Whether to append a snapshot of the page as it is now.</param>
    /// <param name="cancellationToken">The tool call's token.</param>
    /// <remarks>
    /// A failed action is returned untouched. Its message is about why nothing happened, and rows
    /// describing a page that did not change would only bury it. A result that is not a single
    /// block of text, such as a screenshot, is left alone for the same reason.
    /// </remarks>
    public async Task<CallToolResult> AppendToAsync(
        CallToolResult result, bool withSnapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsError == true || result.Content is not [TextContentBlock first])
            return result;

        var text = new StringBuilder(first.Text);
        var pending = _pageService.Dialogs?.PeekPendingDialog();

        // The action that opens a dialog already reports it in full, so the row would say it twice.
        var dialogReported = pending is not null
            && first.Text.Contains("handle_dialog", StringComparison.Ordinal)
            && first.Text.Contains(pending.Message, StringComparison.Ordinal);

        try
        {
            foreach (var row in await RowsAsync(first.Text, pending, dialogReported, cancellationToken)
                .ConfigureAwait(false))
                text.Append('\n').Append(row);
        }
        catch (Exception)
        {
            // The action succeeded. Whatever went wrong describing what it did, the agent is better
            // served by the line saying the action worked than by an error saying it did not.
            return result;
        }

        if (withSnapshot)
            text.Append("\n\n").Append(await SnapshotAsync(pending, cancellationToken).ConfigureAwait(false));

        return new CallToolResult
        {
            Content = [new TextContentBlock { Text = text.ToString() }],
            StructuredContent = result.StructuredContent,
        };
    }

    public void Dispose()
    {
        if (!_subscribed)
            return;

        _page.Popup -= OnPopup;
        _subscribed = false;
    }

    private async Task<IReadOnlyList<string>> RowsAsync(
        string firstLine, IDialog? pending, bool dialogReported, CancellationToken cancellationToken)
    {
        var rows = new List<string>();

        if (pending is not null && !dialogReported)
            rows.Add(DialogNotice.Pending(pending));

        // Waited for first, because the same pause is what lets a navigation the action started
        // commit: describing the page before it would name the page the agent has just left.
        var newTabs = await NewTabsAsync(pending is not null, cancellationToken).ConfigureAwait(false);

        var url = _page.Url;
        var title = pending is null ? await PageDescription.TryTitleAsync(_page).ConfigureAwait(false) : _title;
        var moved = !string.Equals(url, _url, StringComparison.Ordinal);

        // The navigation tools name the page they ended up on in their own first line, so repeating
        // it underneath would be the same sentence twice.
        var alreadySaid = firstLine.Contains(url, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(title) || firstLine.Contains(title, StringComparison.Ordinal));

        if ((moved || !string.Equals(title, _title, StringComparison.Ordinal)) && !alreadySaid)
            rows.Add("Page: " + PageDescription.Of(url, title));

        foreach (var tab in newTabs)
            rows.Add($"New tab opened: [{tab.Index}] {tab.Page.Url}");

        if (ConsoleRow() is { } console)
            rows.Add(console);

        if (moved && _hadSnapshot)
            rows.Add("Refs from the last snapshot no longer address this page: it navigated. Take a new snapshot.");

        return rows;
    }

    /// <summary>
    /// The count of errors the action logged, with the cursor that reads exactly those. The count
    /// is what decides whether the agent should look; the entries themselves are a call away and
    /// are usually long.
    /// </summary>
    private string? ConsoleRow()
    {
        if (_pageService.ConsoleLog is not { } console)
            return null;

        var errors = 0;
        var pageErrors = 0;
        foreach (var entry in console.Read(_consoleCursor).Entries)
        {
            if (entry.Type == ConsoleService.PageErrorType)
                pageErrors++;
            else if (entry.Type == ConsoleService.ErrorType)
                errors++;
        }

        if (errors + pageErrors == 0)
            return null;

        var counted = new List<string>(2);
        if (errors > 0)
            counted.Add($"{errors} {(errors == 1 ? "error" : "errors")}");
        if (pageErrors > 0)
            counted.Add($"{pageErrors} page {(pageErrors == 1 ? "error" : "errors")}");

        return $"Console: {string.Join(", ", counted)}. Read them with console_messages since={_consoleCursor}.";
    }

    /// <summary>
    /// The tabs that were not open when the action started, with the index <c>tab_select</c> takes.
    /// </summary>
    /// <param name="blocked">
    /// Whether a dialog is waiting. Nothing more is coming from a page that is stopped on one, so
    /// there is nothing to wait for and the agent should hear about the dialog straight away.
    /// </param>
    /// <param name="cancellationToken">The tool call's token.</param>
    private async Task<IReadOnlyList<(int Index, IPage Page)>> NewTabsAsync(
        bool blocked, CancellationToken cancellationToken)
    {
        if (_tabsBefore is null)
            return [];

        var opened = blocked
            ? await DiffTabsAsync(cancellationToken).ConfigureAwait(false)
            : await WaitForTabsAsync(cancellationToken).ConfigureAwait(false);
        if (opened.Count == 0)
            return [];

        // A tab exists before the document it was opened for arrives, so its address is still
        // about:blank at the moment it appears. Reporting that would point the agent at nothing.
        var started = Stopwatch.GetTimestamp();
        while (!blocked
            && opened.Any(tab => IsBlank(tab.Page.Url))
            && Stopwatch.GetElapsedTime(started) < NewTabUrlWait
            && !cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(Poll, CancellationToken.None).ConfigureAwait(false);
        }

        return opened;
    }

    /// <summary>
    /// Waits for the open tabs to differ from the ones the action started with.
    /// </summary>
    /// <remarks>
    /// The wait is short, because every action pays it and most actions open nothing. A popup
    /// event extends it: that is the browser saying a tab is on its way, so giving up on the
    /// ordinary budget would be giving up on something known to be coming.
    /// </remarks>
    private async Task<IReadOnlyList<(int Index, IPage Page)>> WaitForTabsAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();

        while (true)
        {
            var opened = await DiffTabsAsync(cancellationToken).ConfigureAwait(false);

            var budget = PopupCount > 0 ? SettleWait + NewTabUrlWait : SettleWait;
            if (opened.Count > 0
                || Stopwatch.GetElapsedTime(started) >= budget
                || cancellationToken.IsCancellationRequested)
                return opened;

            await Task.Delay(Poll, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>The open tabs that were not open when the action started, read once.</summary>
    private async Task<List<(int Index, IPage Page)>> DiffTabsAsync(CancellationToken cancellationToken)
    {
        var tabs = await TryListTabsAsync(_pageService, cancellationToken).ConfigureAwait(false) ?? [];

        var opened = new List<(int Index, IPage Page)>();
        for (var index = 0; index < tabs.Count; index++)
        {
            if (!_tabsBefore!.Any(before => ReferenceEquals(before, tabs[index])))
                opened.Add((index, tabs[index]));
        }

        return opened;
    }

    /// <summary>How many popups the page announced while the action ran.</summary>
    private int PopupCount
    {
        get { lock (_popups) return _popups.Count; }
    }

    private static bool IsBlank(string url)
        => string.IsNullOrEmpty(url) || url == "about:blank";

    private void OnPopup(object? sender, IPage popup)
    {
        // The event fires on a browser thread, and the wait reads the count from the tool's.
        lock (_popups)
            _popups.Add(popup);
    }

    private async Task<string> SnapshotAsync(IDialog? pending, CancellationToken cancellationToken)
    {
        if (pending is not null)
            return "Snapshot not taken: the page is waiting on a dialog.";

        try
        {
            var tree = await _pageService.GetSnapshotService(_page)
                .TakeSnapshotAsync(_pageService.GetActiveFrame(), rootRef: null, maxDepth: null, cancellationToken)
                .ConfigureAwait(false);

            return "Snapshot:\n" + tree;
        }
        catch (Exception ex)
        {
            // The action itself succeeded, so this is a note under it rather than a failure.
            return $"Snapshot not taken: {ex.Message}";
        }
    }

    /// <summary>
    /// The open tabs, or null when there is no browser to ask. Nothing here may start one: the
    /// report describes what an action did, and launching a browser to describe a fake page is
    /// not that.
    /// </summary>
    private static async Task<IReadOnlyList<IPage>?> TryListTabsAsync(
        ActivePageService pageService, CancellationToken cancellationToken)
    {
        if (!pageService.IsBrowserLaunched)
            return null;

        try
        {
            return await pageService.ListTabsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
