using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;

namespace Motus.Tests.Transport;

/// <summary>
/// Thread-safe variant of <see cref="FakeCdpSocket"/> for concurrent stress tests.
/// Uses concurrent collections for sent messages and auto-responses.
/// </summary>
internal sealed class ConcurrentFakeCdpSocket : ICdpSocket
{
    private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>();
    private readonly ConcurrentQueue<byte[]> _sent = new();
    private readonly ConcurrentQueue<string> _autoResponses = new();

    public bool IsOpen { get; private set; } = true;

    public Task ConnectAsync(Uri endpointUri, CancellationToken ct)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
    {
        _sent.Enqueue(message.ToArray());
        if (_autoResponses.TryDequeue(out var response))
            Enqueue(response);
        return Task.CompletedTask;
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
    {
        if (!IsOpen)
            return ReadOnlyMemory<byte>.Empty;

        byte[] msg;
        try
        {
            msg = await _inbox.Reader.ReadAsync(ct);
        }
        catch (ChannelClosedException)
        {
            return ReadOnlyMemory<byte>.Empty;
        }

        return msg;
    }

    internal void Enqueue(string json)
        => _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(json));

    internal void QueueResponse(string json)
        => _autoResponses.Enqueue(json);

    internal void SimulateDisconnect()
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
    }

    internal IReadOnlyList<byte[]> GetSentMessages() => _sent.ToArray();

    internal string GetSentJson(int index) => Encoding.UTF8.GetString(_sent.ToArray()[index]);

    internal int SentCount => _sent.Count;

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
