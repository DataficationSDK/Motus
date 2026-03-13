using System.Text;
using System.Threading.Channels;
using Motus;

namespace Motus.Recorder.Tests.Transport;

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

    internal void Enqueue(string json)
        => _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(json));

    internal void QueueResponse(string json)
        => _autoResponses.Enqueue(json);

    internal void SimulateDisconnect()
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
    }

    internal IReadOnlyList<byte[]> SentMessages => _sent;

    internal string GetSentJson(int index) => Encoding.UTF8.GetString(_sent[index]);

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
