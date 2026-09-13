using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Polls the browser's HTTP endpoint until the CDP WebSocket URL is available.
/// </summary>
/// <remarks>
/// One poller serves one wait, so the reason the last attempt failed belongs to that wait alone
/// and two connections opened at once cannot read each other's.
/// </remarks>
internal sealed class CdpEndpointPoller
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Why the most recent attempt to read the endpoint did not produce a WebSocket URL, or null
    /// while no attempt has failed.
    /// </summary>
    /// <remarks>
    /// Every attempt is allowed to fail: a browser that is still starting refuses the connection,
    /// and that is the ordinary case rather than news. Only the last failure is kept, and only so
    /// that a caller giving up has something to say beyond how long it waited. A refused
    /// connection, a name that will not resolve and an answer from something else listening on
    /// that port all look identical from the outside and call for different next steps.
    /// </remarks>
    internal Exception? LastAttemptError { get; private set; }

    /// <summary>
    /// What the last failed attempt said, worded to follow a sentence, or nothing when no attempt
    /// has failed yet.
    /// </summary>
    internal string Describe()
        => LastAttemptError is null ? string.Empty : $" The last attempt to reach it said: {LastAttemptError.Message}";

    internal Task<Uri> WaitForEndpointAsync(int port, TimeSpan timeout, CancellationToken ct)
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
    internal async Task<Uri> WaitForEndpointAsync(Uri httpEndpoint, TimeSpan timeout, CancellationToken ct)
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
                if (doc.RootElement.TryGetProperty("webSocketDebuggerUrl", out var wsProp)
                    && wsProp.GetString() is { } wsUrl)
                {
                    return new Uri(wsUrl);
                }

                // Something is listening and willing to answer, but it is not a browser offering
                // CDP, which is worth saying rather than waiting out in silence.
                LastAttemptError = new InvalidOperationException(
                    $"{url} answered without a webSocketDebuggerUrl.");
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Browser not ready yet
                LastAttemptError = ex;
            }

            await Task.Delay(PollInterval, ct).ConfigureAwait(false);
        }

        throw new MotusTimeoutException(
            timeoutDuration: timeout,
            message: $"Browser did not provide a CDP endpoint within {timeout.TotalSeconds}s "
                     + $"at {httpEndpoint}.{Describe()}");
    }
}
