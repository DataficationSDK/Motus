namespace Motus;

/// <summary>
/// Abstraction over a WebSocket connection for testability.
/// The transport layer consumes this interface rather than <see cref="System.Net.WebSockets.ClientWebSocket"/> directly.
/// </summary>
internal interface ICdpSocket : IAsyncDisposable
{
    /// <summary>
    /// Whether the socket is currently in an open state.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Connects to the specified CDP WebSocket endpoint.
    /// </summary>
    Task ConnectAsync(Uri endpointUri, CancellationToken ct);

    /// <summary>
    /// Sends a complete text message.
    /// </summary>
    Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct);

    /// <summary>
    /// Receives one complete WebSocket message, assembling frames as needed.
    /// Returns the message bytes, or an empty span if the socket closed cleanly.
    /// The returned memory is valid until the next call to <see cref="ReceiveAsync"/>.
    /// </summary>
    ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct);
}
