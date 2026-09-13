using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Motus;

/// <summary>
/// Internal abstraction over a protocol session scoped to a single browser target.
/// Implementors handle command dispatch and event delivery for a specific protocol (CDP, BiDi, etc.).
/// </summary>
internal interface IMotusSession
{
    /// <summary>
    /// The session ID, or <c>null</c> for the browser-level session.
    /// </summary>
    string? SessionId { get; }

    /// <summary>
    /// The transport capabilities available to this session.
    /// </summary>
    MotusCapabilities Capabilities { get; }

    /// <summary>
    /// Sends a typed command and returns the typed response.
    /// </summary>
    Task<TResponse> SendAsync<TParams, TResponse>(
        string method,
        TParams command,
        JsonTypeInfo<TParams> paramsTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct);

    /// <summary>
    /// Sends a command with no parameters and returns the typed response.
    /// </summary>
    Task<TResponse> SendAsync<TResponse>(
        string method,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct);

    /// <summary>
    /// Sends a command with no meaningful response (fire-and-forget with ack).
    /// </summary>
    Task SendAsync<TParams>(
        string method,
        TParams command,
        JsonTypeInfo<TParams> paramsTypeInfo,
        CancellationToken ct);

    /// <summary>
    /// Subscribes to a protocol event, returning deserialized events scoped to this session.
    /// </summary>
    IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(
        string eventKey,
        JsonTypeInfo<TEvent> eventTypeInfo,
        CancellationToken ct);

    /// <summary>
    /// Subscribes to several protocol events at once, scoped to this session, and hands them back
    /// with the name of the event each one is.
    /// </summary>
    /// <remarks>
    /// Meant for events that describe one thing between them, where the order they arrived in is
    /// part of the meaning. A transport that can promise that order overrides this; the fallback
    /// here reads each event on its own and merges what arrives, which keeps every event but not
    /// the order between different ones.
    /// </remarks>
    IAsyncEnumerable<RawCdpEvent> SubscribeAsync(IReadOnlyList<string> eventKeys, CancellationToken ct)
        => MergeAsync(this, eventKeys, ct);

    /// <summary>
    /// Releases event channel resources associated with this session.
    /// Called when a page or session is torn down.
    /// </summary>
    void CleanupChannels();

    private static async IAsyncEnumerable<RawCdpEvent> MergeAsync(
        IMotusSession session, IReadOnlyList<string> eventKeys, [EnumeratorCancellation] CancellationToken ct)
    {
        var merged = Channel.CreateUnbounded<RawCdpEvent>();
        var pumps = new List<Task>(eventKeys.Count);

        foreach (var key in eventKeys)
            pumps.Add(PumpAsync(key));

        _ = Task.WhenAll(pumps).ContinueWith(
            _ => merged.Writer.TryComplete(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        await foreach (var evt in merged.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            yield return evt;

        async Task PumpAsync(string key)
        {
            try
            {
                await foreach (var element in session.SubscribeAsync(key, CdpJsonContext.Default.JsonElement, ct)
                    .ConfigureAwait(false))
                {
                    await merged.Writer.WriteAsync(new RawCdpEvent(key, element, session.SessionId), ct)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // The subscriber has gone; the merged channel completes with the rest.
            }
        }
    }
}
