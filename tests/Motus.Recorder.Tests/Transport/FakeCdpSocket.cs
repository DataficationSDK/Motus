using System.Text;
using System.Threading.Channels;
using Motus;

namespace Motus.Recorder.Tests.Transport;

/// <summary>
/// Mock <see cref="ICdpSocket"/> for unit tests. Provides controllable inbound message
/// delivery and captures all outbound sends.
/// </summary>
/// <remarks>
/// A command that only bookkeeps the connection is answered from <see cref="CdpFakeResponse"/>
/// without touching anything the fixture set up, so adding one to browser startup does not shift
/// the fixture. Otherwise the next response queued with <see cref="QueueResponse"/> is handed
/// back, readdressed to the command that just went out.
/// </remarks>
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
        var bytes = message.ToArray();
        _sent.Add(bytes);

        // Both answers below are written inside SendRawAsync, after the pending request is
        // registered and before it is awaited, so the response reaches the command that just went
        // out.
        if (CdpFakeResponse.TryAnswerBookkeeping(bytes, out var bookkeeping))
        {
            Enqueue(bookkeeping);
            return Task.CompletedTask;
        }

        if (_autoResponses.TryDequeue(out var response))
            Enqueue(CdpFakeResponse.WithIdOf(bytes, response));
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

    internal IReadOnlyList<byte[]> SentMessages => _sent;

    internal string GetSentJson(int index) => Encoding.UTF8.GetString(_sent[index]);

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
