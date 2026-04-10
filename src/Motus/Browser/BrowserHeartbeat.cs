namespace Motus;

/// <summary>
/// Background monitor that periodically pings the browser to detect freezes.
/// When Chrome stops responding to CDP commands (without closing the WebSocket),
/// the heartbeat detects the freeze and fires a callback so the browser can be
/// marked as disconnected and pending commands faulted.
/// </summary>
internal sealed class BrowserHeartbeat : IAsyncDisposable
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(10);

    private readonly IMotusSession _session;
    private readonly Action<Exception?> _onUnhealthy;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    internal BrowserHeartbeat(IMotusSession session, Action<Exception?> onUnhealthy)
    {
        _session = session;
        _onUnhealthy = onUnhealthy;
    }

    internal void Start()
    {
        _loop = RunAsync(_cts.Token);
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HeartbeatInterval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                pingCts.CancelAfter(PingTimeout);

                await _session.SendAsync(
                    "Browser.getVersion",
                    CdpJsonContext.Default.BrowserGetVersionResult,
                    pingCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Intentional shutdown, do not report
                return;
            }
            catch (Exception ex)
            {
                // Ping timed out or failed: browser is unresponsive
                _onUnhealthy(ex);
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _cts.Dispose();
    }
}
