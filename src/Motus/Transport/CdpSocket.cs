using System.Buffers;
using System.Net.WebSockets;

namespace Motus;

/// <summary>
/// Real WebSocket adapter wrapping <see cref="ClientWebSocket"/>.
/// Handles frame assembly for messages that span multiple WebSocket frames.
/// Manages its own receive buffer internally, growing as needed to accommodate
/// large CDP responses (e.g. base64-encoded screenshots).
/// </summary>
internal sealed class CdpSocket : ICdpSocket
{
    private const int InitialBufferSize = 16 * 1024;

    private readonly ClientWebSocket _ws = new();
    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
    private bool _disposed;

    public bool IsOpen => !_disposed && _ws.State == WebSocketState.Open;

    public Task ConnectAsync(Uri endpointUri, CancellationToken ct)
        => _ws.ConnectAsync(endpointUri, ct);

    public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _ws.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, ct).ConfigureAwait(false);
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalBytes = 0;

        while (true)
        {
            if (totalBytes >= _buffer.Length)
            {
                // Grow the buffer, preserving all bytes read so far
                var newBuffer = ArrayPool<byte>.Shared.Rent(_buffer.Length * 2);
                _buffer.AsSpan(0, totalBytes).CopyTo(newBuffer);
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = newBuffer;
            }

            var result = await _ws.ReceiveAsync(
                _buffer.AsMemory(totalBytes), ct).ConfigureAwait(false);

            if (result.MessageType == WebSocketMessageType.Close)
                return ReadOnlyMemory<byte>.Empty;

            totalBytes += result.Count;

            if (result.EndOfMessage)
                return new ReadOnlyMemory<byte>(_buffer, 0, totalBytes);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_ws.State == WebSocketState.Open)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort graceful close; swallow errors during shutdown.
            }
        }

        ArrayPool<byte>.Shared.Return(_buffer);
        _ws.Dispose();
    }
}
