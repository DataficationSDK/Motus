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
    private readonly CdpFakeLedger _ledger = new();

    public bool IsOpen { get; private set; } = true;

    public Task ConnectAsync(Uri endpointUri, CancellationToken ct)
    {
        IsOpen = true;
        return Task.CompletedTask;
    }

    public Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
    {
        var bytes = message.ToArray();
        _sent.Enqueue(bytes);
        _ledger.Sent(bytes);

        if (CdpFakeResponse.TryAnswerBookkeeping(bytes, out var bookkeeping))
        {
            Deliver(bytes, bookkeeping);
            return Task.CompletedTask;
        }

        if (_autoResponses.TryDequeue(out var response))
            Deliver(bytes, CdpFakeResponse.WithIdOf(bytes, response));

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

    /// <summary>
    /// Enqueues a JSON string to be received by the transport immediately. A response is
    /// readdressed to the newest command still waiting for an answer; an event is delivered as
    /// written.
    /// </summary>
    internal void Enqueue(string json)
        => Write(_ledger.Correlate(json));

    /// <summary>
    /// Enqueues a JSON string exactly as written, including its <c>id</c>.
    /// </summary>
    internal void EnqueueRaw(string json)
    {
        _ledger.AnsweredBy(json);
        Write(json);
    }

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

    private void Deliver(ReadOnlySpan<byte> command, string response)
    {
        if (CdpFakeResponse.CarriesId(response))
            _ledger.Answered(command);

        Write(response);
    }

    private void Write(string json)
        => _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(json));
}
