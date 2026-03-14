using System.Threading.Channels;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Manages a pool of browser instances with configurable min/max capacity.
/// Uses a semaphore for capacity control and a channel as the idle queue.
/// </summary>
internal sealed class BrowserPool : IBrowserPool
{
    private readonly BrowserPoolOptions _options;
    private readonly SemaphoreSlim _capacitySemaphore;
    private readonly Channel<IBrowser> _idleChannel = Channel.CreateUnbounded<IBrowser>();
    private int _activeCount;
    private int _idleCount;
    private int _disposed;

    internal BrowserPool(BrowserPoolOptions options)
    {
        _options = options;
        _capacitySemaphore = new SemaphoreSlim(options.MaxInstances, options.MaxInstances);
    }

    public int ActiveCount => Volatile.Read(ref _activeCount);

    public int IdleCount => Volatile.Read(ref _idleCount);

    internal async Task WarmUpAsync(CancellationToken ct)
    {
        var tasks = new List<Task>();
        for (var i = 0; i < _options.MinInstances; i++)
        {
            tasks.Add(WarmUpOneAsync(ct));
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task WarmUpOneAsync(CancellationToken ct)
    {
        await _capacitySemaphore.WaitAsync(ct).ConfigureAwait(false);
        var browser = await MotusLauncher.LaunchAsync(_options.LaunchOptions, ct).ConfigureAwait(false);
        SubscribeDisconnected(browser);
        Interlocked.Increment(ref _idleCount);
        _idleChannel.Writer.TryWrite(browser);
    }

    public async Task<IBrowserLease> AcquireAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        // Try to grab an idle browser without blocking
        while (_idleChannel.Reader.TryRead(out var idle))
        {
            Interlocked.Decrement(ref _idleCount);
            if (idle.IsConnected)
            {
                Interlocked.Increment(ref _activeCount);
                return new BrowserLease(idle, ReturnAsync);
            }
            // Disconnected browser; discard and release capacity
            await idle.DisposeAsync().ConfigureAwait(false);
            _capacitySemaphore.Release();
        }

        // No idle browser available; try to launch a new one within capacity
        using var timeoutCts = new CancellationTokenSource(_options.AcquireTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        if (!await _capacitySemaphore.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            // At capacity. Wait for either a return or a timeout.
            // Try reading from idle channel (a browser may be returned while we wait)
            using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            while (true)
            {
                // Try idle channel again
                if (_idleChannel.Reader.TryRead(out var returned))
                {
                    Interlocked.Decrement(ref _idleCount);
                    if (returned.IsConnected)
                    {
                        Interlocked.Increment(ref _activeCount);
                        return new BrowserLease(returned, ReturnAsync);
                    }
                    await returned.DisposeAsync().ConfigureAwait(false);
                    _capacitySemaphore.Release();
                }

                // Try to acquire capacity with timeout
                if (await _capacitySemaphore.WaitAsync(_options.AcquireTimeout, delayCts.Token).ConfigureAwait(false))
                    break;

                throw new TimeoutException(
                    $"Could not acquire a browser from the pool within {_options.AcquireTimeout.TotalSeconds}s. " +
                    $"Max instances: {_options.MaxInstances}.");
            }
        }

        // We have capacity; check idle one more time before launching
        if (_idleChannel.Reader.TryRead(out var recheck))
        {
            Interlocked.Decrement(ref _idleCount);
            if (recheck.IsConnected)
            {
                _capacitySemaphore.Release();
                Interlocked.Increment(ref _activeCount);
                return new BrowserLease(recheck, ReturnAsync);
            }
            await recheck.DisposeAsync().ConfigureAwait(false);
            // Capacity already consumed for this launch attempt
        }

        var browser = await MotusLauncher.LaunchAsync(_options.LaunchOptions, linkedCts.Token).ConfigureAwait(false);
        SubscribeDisconnected(browser);
        Interlocked.Increment(ref _activeCount);
        return new BrowserLease(browser, ReturnAsync);
    }

    private ValueTask ReturnAsync(IBrowser browser)
    {
        Interlocked.Decrement(ref _activeCount);

        if (browser.IsConnected && Volatile.Read(ref _disposed) == 0)
        {
            Interlocked.Increment(ref _idleCount);
            _idleChannel.Writer.TryWrite(browser);
            return ValueTask.CompletedTask;
        }

        // Disconnected or pool disposed; discard the browser
        _capacitySemaphore.Release();
        return browser.DisposeAsync();
    }

    private void SubscribeDisconnected(IBrowser browser)
    {
        browser.Disconnected += OnBrowserDisconnected;
    }

    private void OnBrowserDisconnected(object? sender, EventArgs e)
    {
        // Nothing to do actively here. The browser will be detected as disconnected
        // when it is dequeued from the idle channel or when the lease is returned.
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        _idleChannel.Writer.TryComplete();

        while (_idleChannel.Reader.TryRead(out var browser))
        {
            Interlocked.Decrement(ref _idleCount);
            await browser.DisposeAsync().ConfigureAwait(false);
        }

        _capacitySemaphore.Dispose();
    }
}
