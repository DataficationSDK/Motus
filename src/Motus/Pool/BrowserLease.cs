using Motus.Abstractions;

namespace Motus;

/// <summary>
/// A leased browser that returns itself to the pool on dispose.
/// Disconnected browsers are discarded instead of recycled.
/// </summary>
internal sealed class BrowserLease : IBrowserLease
{
    private readonly Func<IBrowser, ValueTask> _returnAction;
    private int _disposed;

    internal BrowserLease(IBrowser browser, Func<IBrowser, ValueTask> returnAction)
    {
        Browser = browser;
        _returnAction = returnAction;
    }

    public IBrowser Browser { get; }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        await _returnAction(Browser);
    }
}
