using System.Text;
using System.Threading.Channels;

namespace Motus.Tests.Transport;

/// <summary>
/// Mock <see cref="ICdpSocket"/> for unit tests. Provides controllable inbound message
/// delivery and captures all outbound sends.
/// </summary>
internal sealed class FakeCdpSocket : ICdpSocket
{
    private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte[]> _sent = new();
    private readonly Queue<string> _autoResponses = new();

    public bool IsOpen { get; private set; } = true;

    public Task ConnectAsync(Uri endpointUri, CancellationToken ct)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
    {
        _sent.Add(message.ToArray());
        // Dequeue auto-response if available. This runs inside SendRawAsync,
        // after the TCS is registered in _pending but before await tcs.Task,
        // guaranteeing the response is dispatched to the correct pending request.
        if (_autoResponses.TryDequeue(out var response))
            Enqueue(response);
        return Task.CompletedTask;
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (!IsOpen)
            return 0;

        byte[] msg;
        try
        {
            msg = await _inbox.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return 0;
        }

        msg.CopyTo(buffer);
        return msg.Length;
    }

    /// <summary>
    /// Enqueues a JSON string to be received by the transport immediately.
    /// </summary>
    internal void Enqueue(string json)
        => _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(json));

    /// <summary>
    /// Queues a response to be delivered on the next outbound send.
    /// This is safe for multi-step CDP sequences because the response is
    /// enqueued inside <see cref="SendAsync"/>, after the transport registers
    /// the pending TCS but before it awaits the result.
    /// </summary>
    internal void QueueResponse(string json)
        => _autoResponses.Enqueue(json);

    /// <summary>
    /// Simulates a clean WebSocket disconnect.
    /// </summary>
    internal void SimulateDisconnect()
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
    }

    /// <summary>
    /// All messages sent through this socket, as raw byte arrays.
    /// </summary>
    internal IReadOnlyList<byte[]> SentMessages => _sent;

    /// <summary>
    /// Decodes a sent message as UTF-8 string.
    /// </summary>
    internal string GetSentJson(int index) => Encoding.UTF8.GetString(_sent[index]);

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
