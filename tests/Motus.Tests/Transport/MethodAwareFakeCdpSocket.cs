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
/// Tests use <see cref="Respond"/> to pre-register deterministic responses by method (for example
/// the target and session commands that hand back ids a fixture names), and
/// <see cref="SetHandler"/> to compute responses from the method and parameters on the wire.
/// Commands that only set the connection up are answered with an empty result without being
/// registered at all, so a fixture does not have to know how many of them there are.
/// </summary>
internal sealed class MethodAwareFakeCdpSocket : ICdpSocket
{
    /// <summary>
    /// Commands that turn a domain on for a new page or session. They return an empty result and
    /// carry nothing a fixture reads, so answering them here keeps a fixture from having to list
    /// them, and keeps one more of them from breaking every fixture that opens a page.
    /// </summary>
    private static readonly HashSet<string> SessionSetup = new(StringComparer.Ordinal)
    {
        "Page.enable",
        "Runtime.enable",
        "Page.setInterceptFileChooserDialog",
        "Network.enable",
        "Target.setAutoAttach",
    };

    private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _byMethod = new(StringComparer.Ordinal);
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
        var method = root.GetProperty("method").GetString()!;

        string response;
        if (_byMethod.TryGetValue(method, out var queued) && queued.TryDequeue(out var registered))
        {
            response = CdpFakeResponse.AddressedTo(bytes, registered);
        }
        else if (CdpFakeResponse.TryAnswerBookkeeping(bytes, out var bookkeeping))
        {
            response = bookkeeping;
        }
        else if (SessionSetup.Contains(method))
        {
            response = CdpFakeResponse.EmptyResultFor(root);
        }
        else
        {
            var handler = _handler
                ?? throw new InvalidOperationException(
                    $"MethodAwareFakeCdpSocket: no handler and no registered response for {method} (id {id}).");
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

    /// <summary>
    /// Pre-registers a canned response for the next command with this method. Registering more
    /// than one answers successive commands in the order they were registered. The <c>id</c> in
    /// the response is taken from the command, so it can be left out.
    /// </summary>
    internal void Respond(string method, string json)
        => _byMethod.GetOrAdd(method, static _ => new ConcurrentQueue<string>()).Enqueue(json);

    /// <summary>Sets the handler used for any command not covered by <see cref="Respond"/>.</summary>
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
