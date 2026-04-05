using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;

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
    /// Releases event channel resources associated with this session.
    /// Called when a page or session is torn down.
    /// </summary>
    void CleanupChannels();
}
