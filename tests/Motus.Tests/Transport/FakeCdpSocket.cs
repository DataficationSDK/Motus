using System.Text;
using System.Threading.Channels;

namespace Motus.Tests.Transport;

/// <summary>
/// Mock <see cref="ICdpSocket"/> for unit tests. Provides controllable inbound message
/// delivery and captures all outbound sends.
/// </summary>
/// <remarks>
/// An outbound command is answered in three steps. A command that only bookkeeps the connection is
/// answered from <see cref="CdpFakeResponse"/> without touching anything the fixture set up, so
/// adding one to browser startup does not shift the fixture. Otherwise the next response queued
/// with <see cref="QueueResponse"/> is handed back, readdressed to the command that just went out.
/// Anything else is left to the fixture, which hands the socket a response with
/// <see cref="Enqueue"/> after starting the call it answers.
/// </remarks>
internal sealed class FakeCdpSocket : ICdpSocket
{
    private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>();
    private readonly List<byte[]> _sent = new();
    private readonly Queue<string> _autoResponses = new();
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
        _sent.Add(bytes);
        _ledger.Sent(bytes);

        // Both answers below run inside SendRawAsync, after the TCS is registered in _pending but
        // before await tcs.Task, guaranteeing the response is dispatched to the correct pending
        // request.
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
    /// <remarks>
    /// The <c>id</c> written into the response is ignored; see <see cref="CdpFakeLedger"/>. A test
    /// that is about correlation itself uses <see cref="EnqueueRaw"/>.
    /// </remarks>
    internal void Enqueue(string json)
        => Write(_ledger.Correlate(json));

    /// <summary>
    /// Enqueues a JSON string exactly as written, including its <c>id</c>. For tests that assert
    /// how the transport correlates a response to a command.
    /// </summary>
    internal void EnqueueRaw(string json)
    {
        _ledger.AnsweredBy(json);
        Write(json);
    }

    /// <summary>
    /// Queues a response to be delivered on the next outbound send.
    /// This is safe for multi-step CDP sequences because the response is
    /// enqueued inside <see cref="SendAsync"/>, after the transport registers
    /// the pending TCS but before it awaits the result.
    /// </summary>
    /// <remarks>
    /// The <c>id</c> written into the queued JSON is ignored; see <see cref="CdpFakeResponse.WithIdOf"/>.
    /// </remarks>
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

    private void Deliver(ReadOnlySpan<byte> command, string response)
    {
        // A queued event answers nothing, so the command it went out beside is still waiting.
        if (CdpFakeResponse.CarriesId(response))
            _ledger.Answered(command);

        Write(response);
    }

    private void Write(string json)
        => _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(json));
}
