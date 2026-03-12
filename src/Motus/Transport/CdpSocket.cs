using System.Buffers;
using System.Net.WebSockets;

namespace Motus;

/// <summary>
/// Real WebSocket adapter wrapping <see cref="ClientWebSocket"/>.
/// Handles frame assembly for messages that span multiple WebSocket frames.
/// </summary>
internal sealed class CdpSocket : ICdpSocket
{
    private readonly ClientWebSocket _ws = new();
    private bool _disposed;

    public bool IsOpen => !_disposed && _ws.State == WebSocketState.Open;

    public Task ConnectAsync(Uri endpointUri, CancellationToken ct)
        => _ws.ConnectAsync(endpointUri, ct);

    public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _ws.SendAsync(message, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int totalBytes = 0;

        while (true)
        {
            if (totalBytes >= buffer.Length)
                throw new MessageTooLargeException(totalBytes * 2);

            var result = await _ws.ReceiveAsync(buffer[totalBytes..], ct);

            if (result.MessageType == WebSocketMessageType.Close)
                return 0;

            totalBytes += result.Count;

            if (result.EndOfMessage)
                return totalBytes;
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
                await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            }
            catch
            {
                // Best-effort graceful close; swallow errors during shutdown.
            }
        }

        _ws.Dispose();
    }
}
