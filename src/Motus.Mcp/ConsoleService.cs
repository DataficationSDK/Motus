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
/// with the <c>pageerror</c> type. The buffer drains on read, so each read returns
/// only what arrived since the last one, and it is capped so a chatty page cannot
/// grow it without bound.
/// </remarks>
public sealed class ConsoleService
{
    /// <summary>The type assigned to an uncaught page error in the buffer.</summary>
    public const string PageErrorType = "pageerror";

    private const int Capacity = 250;

    private readonly object _lock = new();
    private readonly Queue<ConsoleEntry> _entries = new();

    private IPage? _subscribedPage;

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
    /// Returns the captured entries in arrival order and clears the buffer.
    /// </summary>
    public IReadOnlyList<ConsoleEntry> Drain()
    {
        lock (_lock)
        {
            var drained = _entries.ToArray();
            _entries.Clear();
            return drained;
        }
    }

    private void OnConsole(object? sender, ConsoleMessageEventArgs e)
        => Add(new ConsoleEntry(e.Type, e.Text));

    private void OnPageError(object? sender, PageErrorEventArgs e)
        => Add(new ConsoleEntry(PageErrorType, e.Message));

    private void Add(ConsoleEntry entry)
    {
        lock (_lock)
        {
            if (_entries.Count >= Capacity)
                _entries.Dequeue();
            _entries.Enqueue(entry);
        }
    }
}

/// <summary>A single captured console message or page error.</summary>
/// <param name="Type">The console message type, or <c>pageerror</c> for an uncaught error.</param>
/// <param name="Text">The message text.</param>
public sealed record ConsoleEntry(string Type, string Text)
{
    /// <summary>Renders the entry as a single <c>[type] text</c> line.</summary>
    public override string ToString() => $"[{Type}] {Text}";
}
