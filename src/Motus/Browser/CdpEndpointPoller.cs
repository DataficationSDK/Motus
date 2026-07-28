using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Polls the browser's HTTP endpoint until the CDP WebSocket URL is available.
/// </summary>
internal static class CdpEndpointPoller
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    internal static Task<Uri> WaitForEndpointAsync(int port, TimeSpan timeout, CancellationToken ct)
        => WaitForEndpointAsync(new Uri($"http://127.0.0.1:{port}"), timeout, ct);

    /// <summary>
    /// Resolves the CDP WebSocket URL from a browser's HTTP debugging endpoint, waiting for the
    /// endpoint to start answering if it is not yet up.
    /// </summary>
    /// <remarks>
    /// A browser Motus launched is always on loopback, but one it is asked to connect to may be
    /// a sidecar container or a service on another host, so the endpoint is given rather than
    /// assumed.
    /// </remarks>
    internal static async Task<Uri> WaitForEndpointAsync(Uri httpEndpoint, TimeSpan timeout, CancellationToken ct)
    {
        using var httpClient = new HttpClient { Timeout = RequestTimeout };
        var url = new Uri(httpEndpoint, "/json/version");
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await httpClient.GetStringAsync(url, ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsProp))
                {
                    var wsUrl = wsProp.GetString();
                    if (wsUrl is not null)
                        return new Uri(wsUrl);
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Browser not ready yet
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }

        throw new MotusTimeoutException(
            timeoutDuration: timeout,
            message: $"Browser did not provide a CDP endpoint within {timeout.TotalSeconds}s at {httpEndpoint}.");
    }
}
