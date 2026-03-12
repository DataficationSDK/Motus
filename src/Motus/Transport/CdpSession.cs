using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Motus;

/// <summary>
/// A typed CDP session scoped to a single target (browser, page, or worker).
/// All command sends and event subscriptions are routed through the underlying transport
/// with the appropriate session ID.
/// </summary>
internal sealed class CdpSession
{
    private readonly CdpTransport _transport;

    /// <summary>
    /// The CDP session ID, or <c>null</c> for the browser-level session.
    /// </summary>
    internal string? SessionId { get; }

    internal CdpSession(CdpTransport transport, string? sessionId = null)
    {
        _transport = transport;
        SessionId = sessionId;
    }

    /// <summary>
    /// Sends a typed CDP command and returns the typed response.
    /// Callers must pass <see cref="JsonTypeInfo{T}"/> from <see cref="CdpJsonContext.Default"/>
    /// for NativeAOT compatibility.
    /// </summary>
    internal async Task<TResponse> SendAsync<TParams, TResponse>(
        string method,
        TParams command,
        JsonTypeInfo<TParams> paramsTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct)
    {
        var paramsElement = JsonSerializer.SerializeToElement(command, paramsTypeInfo);
        var resultElement = await _transport.SendRawAsync(method, paramsElement, SessionId, ct);
        return JsonSerializer.Deserialize(resultElement, responseTypeInfo)
               ?? throw new CdpProtocolException($"Null result for {method}");
    }

    /// <summary>
    /// Sends a CDP command that has no parameters and returns the typed response.
    /// </summary>
    internal async Task<TResponse> SendAsync<TResponse>(
        string method,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct)
    {
        var emptyParams = EmptyJsonElement();
        var resultElement = await _transport.SendRawAsync(method, emptyParams, SessionId, ct);
        return JsonSerializer.Deserialize(resultElement, responseTypeInfo)
               ?? throw new CdpProtocolException($"Null result for {method}");
    }

    /// <summary>
    /// Sends a CDP command that has no meaningful response (fire-and-forget with ack).
    /// </summary>
    internal async Task SendAsync<TParams>(
        string method,
        TParams command,
        JsonTypeInfo<TParams> paramsTypeInfo,
        CancellationToken ct)
    {
        var paramsElement = JsonSerializer.SerializeToElement(command, paramsTypeInfo);
        await _transport.SendRawAsync(method, paramsElement, SessionId, ct);
    }

    /// <summary>
    /// Subscribes to a CDP event, returning only events scoped to this session.
    /// </summary>
    internal IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(
        string eventKey,
        JsonTypeInfo<TEvent> eventTypeInfo,
        CancellationToken ct)
    {
        var sessionId = SessionId ?? string.Empty;
        var channelKey = $"{eventKey}|{sessionId}";
        var channel = _transport.GetOrCreateEventChannel(channelKey);
        return DeserializeEvents(channel.Reader, eventTypeInfo, ct);
    }

    private static JsonElement EmptyJsonElement() => CdpTransport.EmptyJsonElement();

    private static async IAsyncEnumerable<TEvent> DeserializeEvents<TEvent>(
        ChannelReader<RawCdpEvent> reader,
        JsonTypeInfo<TEvent> typeInfo,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var raw in reader.ReadAllAsync(ct))
        {
            var deserialized = JsonSerializer.Deserialize(raw.Params, typeInfo);
            if (deserialized is not null)
                yield return deserialized;
        }
    }
}
