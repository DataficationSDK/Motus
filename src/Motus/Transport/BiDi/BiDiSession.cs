using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Channels;

namespace Motus;

/// <summary>
/// BiDi session scoped to a single browsing context.
/// Implements <see cref="IMotusSession"/> by translating CDP method calls to BiDi
/// equivalents, allowing the engine layer to use BiDi transparently.
/// </summary>
internal sealed class BiDiSession : IMotusSession
{
    private readonly BiDiTransport _transport;
    private readonly HashSet<string> _subscribedBiDiEvents = new();
    private readonly object _subscribeLock = new();

    public string? SessionId { get; }

    internal BiDiTransport Transport => _transport;

    internal BiDiSession(BiDiTransport transport, string? sessionId = null)
    {
        _transport = transport;
        SessionId = sessionId;
    }

    public async Task<TResponse> SendAsync<TParams, TResponse>(
        string method,
        TParams command,
        JsonTypeInfo<TParams> paramsTypeInfo,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct)
    {
        try
        {
            var cdpParams = JsonSerializer.SerializeToElement(command, paramsTypeInfo);
            var cdpResult = await TranslateAndSendAsync(method, cdpParams, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(cdpResult, responseTypeInfo)
                   ?? throw new BiDiProtocolException($"Null result translating {method}");
        }
        catch (BiDiProtocolException bidiEx)
        {
            throw new Abstractions.MotusProtocolException(
                cdpErrorCode: null, commandSent: method,
                message: bidiEx.Message, innerException: bidiEx);
        }
        catch (BiDiDisconnectedException discEx)
        {
            throw new Abstractions.MotusTargetClosedException(
                targetType: "context", targetId: SessionId,
                message: discEx.Message, innerException: discEx);
        }
    }

    public async Task<TResponse> SendAsync<TResponse>(
        string method,
        JsonTypeInfo<TResponse> responseTypeInfo,
        CancellationToken ct)
    {
        try
        {
            var cdpResult = await TranslateAndSendAsync(method, JsonBuilder.Empty(), ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(cdpResult, responseTypeInfo)
                   ?? throw new BiDiProtocolException($"Null result translating {method}");
        }
        catch (BiDiProtocolException bidiEx)
        {
            throw new Abstractions.MotusProtocolException(
                cdpErrorCode: null, commandSent: method,
                message: bidiEx.Message, innerException: bidiEx);
        }
        catch (BiDiDisconnectedException discEx)
        {
            throw new Abstractions.MotusTargetClosedException(
                targetType: "context", targetId: SessionId,
                message: discEx.Message, innerException: discEx);
        }
    }

    public async Task SendAsync<TParams>(
        string method,
        TParams command,
        JsonTypeInfo<TParams> paramsTypeInfo,
        CancellationToken ct)
    {
        try
        {
            var cdpParams = JsonSerializer.SerializeToElement(command, paramsTypeInfo);
            await TranslateAndSendAsync(method, cdpParams, ct).ConfigureAwait(false);
        }
        catch (BiDiProtocolException bidiEx)
        {
            throw new Abstractions.MotusProtocolException(
                cdpErrorCode: null, commandSent: method,
                message: bidiEx.Message, innerException: bidiEx);
        }
        catch (BiDiDisconnectedException discEx)
        {
            throw new Abstractions.MotusTargetClosedException(
                targetType: "context", targetId: SessionId,
                message: discEx.Message, innerException: discEx);
        }
    }

    public async IAsyncEnumerable<TEvent> SubscribeAsync<TEvent>(
        string eventKey,
        JsonTypeInfo<TEvent> eventTypeInfo,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var eventTranslation = BiDiEventMap.GetEventTranslation(eventKey);
        var biDiEventName = eventTranslation?.BiDiEventName ?? eventKey;

        await EnsureSubscribedAsync(biDiEventName, ct).ConfigureAwait(false);

        var contextId = SessionId ?? string.Empty;
        var channelKey = $"{biDiEventName}|{contextId}";
        var channel = _transport.GetOrCreateEventChannel(channelKey);

        await foreach (var raw in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            JsonElement cdpParams = eventTranslation is not null
                ? eventTranslation.TranslateEvent(raw.Params)
                : raw.Params;

            var deserialized = JsonSerializer.Deserialize(cdpParams, eventTypeInfo);
            if (deserialized is not null)
                yield return deserialized;
        }
    }

    public void CleanupChannels()
    {
        if (SessionId is not null)
            _transport.RemoveChannelsForContext(SessionId);
    }

    private async Task<JsonElement> TranslateAndSendAsync(
        string cdpMethod, JsonElement cdpParams, CancellationToken ct)
    {
        if (!BiDiTranslationRegistry.TryGet(cdpMethod, out var translation))
            throw new NotSupportedException(
                $"BiDi transport has no translation for CDP method '{cdpMethod}'.");

        var bidiParams = translation!.TranslateParams(cdpParams, SessionId);
        var bidiResult = await _transport.SendRawAsync(translation.BiDiMethod, bidiParams, ct)
            .ConfigureAwait(false);
        return translation.TranslateResult(bidiResult);
    }

    private async Task EnsureSubscribedAsync(string biDiEventName, CancellationToken ct)
    {
        bool needsSubscribe;
        lock (_subscribeLock)
        {
            needsSubscribe = _subscribedBiDiEvents.Add(biDiEventName);
        }

        if (!needsSubscribe)
            return;

        var subscribeParams = new BiDiSessionSubscribeParams(
            Events: [biDiEventName],
            Contexts: SessionId is not null ? [SessionId] : null);

        var paramsElement = JsonSerializer.SerializeToElement(
            subscribeParams, BiDiJsonContext.Default.BiDiSessionSubscribeParams);

        await _transport.SendRawAsync("session.subscribe", paramsElement, ct)
            .ConfigureAwait(false);
    }
}
