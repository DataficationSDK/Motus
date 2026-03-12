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

    internal static async Task<Uri> WaitForEndpointAsync(int port, TimeSpan timeout, CancellationToken ct)
    {
        using var httpClient = new HttpClient { Timeout = RequestTimeout };
        var url = $"http://127.0.0.1:{port}/json/version";
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await httpClient.GetStringAsync(url, ct);
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

            await Task.Delay(PollInterval, ct);
        }

        throw new MotusTimeoutException(
            timeoutDuration: timeout,
            message: $"Browser did not provide a CDP endpoint within {timeout.TotalSeconds}s on port {port}.");
    }
}
