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
    /// Returns the number of bytes written to <paramref name="buffer"/>, or 0 if the socket closed cleanly.
    /// When the message exceeds <paramref name="buffer"/> length, the implementation must
    /// signal via <see cref="MessageTooLargeException"/> so the caller can retry with a larger buffer.
    /// </summary>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct);
}

/// <summary>
/// Thrown when a received WebSocket message exceeds the provided buffer size.
/// </summary>
internal sealed class MessageTooLargeException : Exception
{
    internal int RequiredSize { get; }

    internal MessageTooLargeException(int requiredSize)
        : base($"Message requires at least {requiredSize} bytes.")
    {
        RequiredSize = requiredSize;
    }
}
