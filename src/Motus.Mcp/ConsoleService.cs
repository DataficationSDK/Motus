using System.Collections.Generic;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Captures the console messages and uncaught errors a page emits, so a later tool
/// call can read them. Tool calls arrive as individually stateless messages and the
/// events fire on a browser thread, so the messages have to accumulate here between
/// the calls that cause them and the call that reads them.
/// </summary>
/// <remarks>
/// The subscription follows the active page: <see cref="Subscribe"/> is called each
/// time the active page is resolved or switched, detaching from the previous page
/// first. Messages and page errors share one bounded buffer; a page error is tagged
/// with the <c>pageerror</c> type. The buffer is capped so a chatty page cannot grow
/// it without bound, and entries leave it only to that cap: every entry carries a
/// sequence number and <see cref="Read"/> takes a cursor, so a read that never
/// reached the agent can simply be made again.
/// </remarks>
public sealed class ConsoleService
{
    /// <summary>The type assigned to an uncaught page error in the buffer.</summary>
    public const string PageErrorType = "pageerror";

    /// <summary>The console type a logged error carries.</summary>
    public const string ErrorType = "error";

    private const int Capacity = 250;

    private readonly object _lock = new();
    private readonly Queue<ConsoleEntry> _entries = new();

    private long _next = 1;
    private IPage? _subscribedPage;

    /// <summary>
    /// The sequence number the next entry will be given. Read before an action and passed back as
    /// the cursor afterwards, it says exactly what that action logged and nothing else.
    /// </summary>
    public long NextSequence
    {
        get { lock (_lock) return _next; }
    }

    /// <summary>
    /// Attaches to the given page's console and error events, detaching from any
    /// previously subscribed page. A repeat call for the same page is a no-op.
    /// </summary>
    public void Subscribe(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (ReferenceEquals(_subscribedPage, page))
            return;

        if (_subscribedPage is not null)
        {
            _subscribedPage.Console -= OnConsole;
            _subscribedPage.PageError -= OnPageError;
        }

        _subscribedPage = page;
        page.Console += OnConsole;
        page.PageError += OnPageError;
    }

    /// <summary>
    /// Returns the captured entries from <paramref name="since"/> onwards in arrival order,
    /// leaving the buffer as it is.
    /// </summary>
    /// <param name="since">
    /// The sequence number to read from, or null for everything the buffer still holds.
    /// </param>
    public LogSlice<ConsoleEntry> Read(long? since = null)
    {
        lock (_lock)
        {
            var from = since is { } cursor && cursor > 1 ? cursor : 1;
            var oldest = _entries.Count == 0 ? _next : _entries.Peek().Sequence;
            var dropped = oldest > from ? (int)(oldest - from) : 0;

            var entries = new List<ConsoleEntry>();
            foreach (var entry in _entries)
            {
                if (entry.Sequence >= from)
                    entries.Add(entry);
            }

            return new LogSlice<ConsoleEntry>(entries, _next, dropped);
        }
    }

    private void OnConsole(object? sender, ConsoleMessageEventArgs e)
        => Add(e.Type, e.Text);

    private void OnPageError(object? sender, PageErrorEventArgs e)
        => Add(PageErrorType, e.Message);

    private void Add(string type, string text)
    {
        lock (_lock)
        {
            if (_entries.Count >= Capacity)
                _entries.Dequeue();
            _entries.Enqueue(new ConsoleEntry(_next++, type, text));
        }
    }
}

/// <summary>A single captured console message or page error.</summary>
/// <param name="Sequence">The entry's position in the log, counting from one and never reused.</param>
/// <param name="Type">The console message type, or <c>pageerror</c> for an uncaught error.</param>
/// <param name="Text">The message text.</param>
public sealed record ConsoleEntry(long Sequence, string Type, string Text)
{
    /// <summary>Whether the entry is an error, logged or uncaught.</summary>
    public bool IsError => Type is ConsoleService.ErrorType or ConsoleService.PageErrorType;

    /// <summary>Renders the entry as a single <c>[type] text</c> line.</summary>
    public override string ToString() => $"[{Type}] {Text}";
}
