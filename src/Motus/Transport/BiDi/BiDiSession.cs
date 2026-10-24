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

    public MotusCapabilities Capabilities => _transport.Capabilities;

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
        var translations = BiDiEventMap.GetEventTranslations(eventKey);
        if (translations.Count > 1)
        {
            await foreach (var merged in SubscribeMergedAsync(translations, eventTypeInfo, ct).ConfigureAwait(false))
                yield return merged;
            yield break;
        }

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

    /// <summary>
    /// Subscribes to every BiDi event that translates to one CDP event, and hands back what any of
    /// them reports, translated.
    /// </summary>
    /// <remarks>
    /// A browser that does not know one of the BiDi events refuses the subscription to it, and the
    /// others are still delivered rather than the whole subscription failing over it.
    /// </remarks>
    private async IAsyncEnumerable<TEvent> SubscribeMergedAsync<TEvent>(
        IReadOnlyList<IBiDiEventTranslation> translations,
        JsonTypeInfo<TEvent> eventTypeInfo,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var contextId = SessionId ?? string.Empty;
        var merged = Channel.CreateUnbounded<JsonElement>();
        var pumps = new List<Task>(translations.Count);

        foreach (var translation in translations)
        {
            try
            {
                await EnsureSubscribedAsync(
                    translation.BiDiEventName, ct, translation.SubscribeToAllContexts).ConfigureAwait(false);
            }
            catch (BiDiProtocolException)
            {
                continue;
            }

            var channel = _transport.GetOrCreateEventChannel($"{translation.BiDiEventName}|{contextId}");
            pumps.Add(PumpAsync(translation, channel.Reader));
        }

        _ = Task.WhenAll(pumps).ContinueWith(
            _ => merged.Writer.TryComplete(), CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        await foreach (var cdpParams in merged.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var deserialized = JsonSerializer.Deserialize(cdpParams, eventTypeInfo);
            if (deserialized is not null)
                yield return deserialized;
        }

        async Task PumpAsync(IBiDiEventTranslation translation, ChannelReader<RawBiDiEvent> reader)
        {
            try
            {
                await foreach (var raw in reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    await merged.Writer.WriteAsync(translation.TranslateEvent(raw.Params), ct)
                        .ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // The subscriber has gone; the merged channel completes with the rest.
            }
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

    /// <param name="biDiEventName">The BiDi event to subscribe to.</param>
    /// <param name="ct">Cancels the subscribe command.</param>
    /// <param name="allContexts">
    /// Subscribes for every browsing context instead of this session's own. Events are still
    /// routed by the context they name, so this session only ever reads its own.
    /// </param>
    private async Task EnsureSubscribedAsync(string biDiEventName, CancellationToken ct, bool allContexts = false)
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
            Contexts: SessionId is not null && !allContexts ? [SessionId] : null);

        var paramsElement = JsonSerializer.SerializeToElement(
            subscribeParams, BiDiJsonContext.Default.BiDiSessionSubscribeParams);

        await _transport.SendRawAsync("session.subscribe", paramsElement, ct)
            .ConfigureAwait(false);
    }
}
