using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace Motus.Tests.Transport;

/// <summary>
/// Fake <see cref="ICdpSocket"/> that inspects each outbound command envelope and produces a
/// response via a test-supplied handler. Unlike <see cref="FakeCdpSocket"/>, which pops responses
/// in FIFO order from a queue and therefore couples content to send order, this socket decouples
/// response content from send order. That matters under heavy parallel sends: threadpool
/// interleaving can assign ids to continuations before the caller's next outer-loop send, so
/// content is not reliably determined by the order responses were enqueued.
///
/// Tests use <see cref="Respond"/> to pre-register deterministic responses by id (for example the
/// browser init and page setup), and <see cref="SetHandler"/> to compute responses from the method
/// and parameters on the wire.
/// </summary>
internal sealed class MethodAwareFakeCdpSocket : ICdpSocket
{
    private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>();
    private readonly ConcurrentDictionary<int, string> _fixedResponses = new();
    private readonly ConcurrentQueue<byte[]> _sent = new();
    private Func<JsonElement, string>? _handler;

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

        using var doc = JsonDocument.Parse(bytes);
        var root = doc.RootElement;
        var id = root.GetProperty("id").GetInt32();

        string response;
        if (_fixedResponses.TryRemove(id, out var fixedResponse))
        {
            response = fixedResponse;
        }
        else
        {
            var handler = _handler
                ?? throw new InvalidOperationException(
                    $"MethodAwareFakeCdpSocket: no handler and no fixed response configured for id {id}.");
            response = handler(root);
        }

        _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(response));
        return Task.CompletedTask;
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
    {
        if (!IsOpen) return ReadOnlyMemory<byte>.Empty;
        try { return await _inbox.Reader.ReadAsync(ct); }
        catch (ChannelClosedException) { return ReadOnlyMemory<byte>.Empty; }
    }

    /// <summary>Pre-registers a canned response for the given id.</summary>
    internal void Respond(int id, string json) => _fixedResponses[id] = json;

    /// <summary>Sets the handler used for any id not covered by <see cref="Respond"/>.</summary>
    internal void SetHandler(Func<JsonElement, string> handler) => _handler = handler;

    /// <summary>Pushes an inbound message directly (for events or out-of-band responses).</summary>
    internal void Enqueue(string json) => _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(json));

    internal IReadOnlyList<byte[]> SentMessages => _sent.ToArray();

    internal string GetSentJson(int index) => Encoding.UTF8.GetString(_sent.ToArray()[index]);

    public ValueTask DisposeAsync()
    {
        IsOpen = false;
        _inbox.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
