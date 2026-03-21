using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Motus;

/// <summary>
/// Core BiDi WebSocket transport. Manages a single WebSocket connection, a background
/// receive loop, request/response correlation, and event channel dispatch.
/// Mirrors <see cref="CdpTransport"/> but uses BiDi message framing (type-discriminated).
/// </summary>
internal sealed class BiDiTransport : IMotusTransport
{
    private readonly ICdpSocket _socket;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoop;
    private bool _disposed;

    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Channel<RawBiDiEvent>> _eventChannels = new();
    private int _nextId;

    /// <inheritdoc />
    public MotusCapabilities Capabilities => MotusCapabilities.AllBiDi;

    /// <summary>
    /// Raised when the WebSocket connection is lost. The exception (if any) is provided.
    /// </summary>
    public event Action<Exception?>? Disconnected;

    internal BiDiTransport(ICdpSocket socket)
    {
        _socket = socket;
    }

    /// <summary>
    /// Connects to the browser BiDi endpoint and starts the background receive loop.
    /// </summary>
    internal async Task ConnectAsync(Uri endpointUri, CancellationToken ct)
    {
        await _socket.ConnectAsync(endpointUri, ct).ConfigureAwait(false);
        _receiveLoop = RunReceiveLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Sends a raw BiDi command and awaits the correlated response.
    /// </summary>
    internal async Task<JsonElement> SendRawAsync(
        string method, JsonElement paramsElement, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(CommandTimeout);
        var effectiveCt = timeoutCts.Token;

        try
        {
            var envelope = new BiDiCommandEnvelope(id, method, paramsElement);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, BiDiJsonContext.Default.BiDiCommandEnvelope);
            await _socket.SendAsync(bytes, effectiveCt).ConfigureAwait(false);

            await using var reg = effectiveCt.Register(() => tcs.TrySetCanceled(effectiveCt));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Gets or creates an event channel for the given composite key.
    /// The key format is <c>"biDiEventName|contextId"</c> (empty string for browser-level).
    /// </summary>
    internal Channel<RawBiDiEvent> GetOrCreateEventChannel(string channelKey)
    {
        return _eventChannels.GetOrAdd(channelKey, static _ =>
            Channel.CreateUnbounded<RawBiDiEvent>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true
            }));
    }

    /// <summary>
    /// Removes and completes all event channels associated with the given browsing context.
    /// Called when a page/context is disposed to prevent unbounded channel accumulation.
    /// </summary>
    internal void RemoveChannelsForContext(string contextId)
    {
        var suffix = $"|{contextId}";
        foreach (var key in _eventChannels.Keys)
        {
            if (key.EndsWith(suffix, StringComparison.Ordinal) &&
                _eventChannels.TryRemove(key, out var channel))
            {
                channel.Writer.TryComplete();
            }
        }
    }

    private async Task RunReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await _socket.ReceiveAsync(ct).ConfigureAwait(false);

                if (message.IsEmpty)
                {
                    FaultAllPending(new BiDiDisconnectedException());
                    CompleteAllChannels();
                    Disconnected?.Invoke(null);
                    return;
                }

                try
                {
                    var discriminator = JsonSerializer.Deserialize(
                        message.Span,
                        BiDiJsonContext.Default.BiDiInboundDiscriminator);

                    if (discriminator is not null)
                        DispatchMessage(discriminator);
                }
                catch (JsonException)
                {
                    // Malformed BiDi message; skip it.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown via dispose
        }
        catch (Exception ex)
        {
            FaultAllPending(new BiDiDisconnectedException(ex));
            CompleteAllChannels();
            Disconnected?.Invoke(ex);
        }
    }

    private void DispatchMessage(BiDiInboundDiscriminator d)
    {
        if (d.Type is "success" or "error" && d.Id is not null)
        {
            DispatchResponse(d);
        }
        else if (d.Type is "event" && d.Method is not null)
        {
            DispatchEvent(d);
        }
    }

    private void DispatchResponse(BiDiInboundDiscriminator d)
    {
        if (!_pending.TryRemove(d.Id!.Value, out var tcs))
            return;

        if (d.Type == "error")
        {
            tcs.TrySetException(new BiDiProtocolException(
                d.Error ?? "unknown error",
                d.Message ?? string.Empty));
        }
        else
        {
            tcs.TrySetResult(d.Result ?? EmptyJsonElement());
        }
    }

    private void DispatchEvent(BiDiInboundDiscriminator d)
    {
        // Extract contextId from params (most BiDi events carry a "context" field)
        string contextId = string.Empty;
        if (d.Params.HasValue && d.Params.Value.ValueKind == JsonValueKind.Object &&
            d.Params.Value.TryGetProperty("context", out var ctx) &&
            ctx.ValueKind == JsonValueKind.String)
        {
            contextId = ctx.GetString() ?? string.Empty;
        }

        var channelKey = $"{d.Method}|{contextId}";
        if (_eventChannels.TryGetValue(channelKey, out var channel))
        {
            channel.Writer.TryWrite(new RawBiDiEvent(d.Params ?? default, contextId));
        }

        // Also route to context-agnostic channel for browser-level subscribers
        if (contextId.Length > 0)
        {
            var wildcardKey = $"{d.Method}|";
            if (_eventChannels.TryGetValue(wildcardKey, out var wildcardChannel))
            {
                wildcardChannel.Writer.TryWrite(new RawBiDiEvent(d.Params ?? default, contextId));
            }
        }
    }

    private static readonly JsonElement s_emptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    internal static JsonElement EmptyJsonElement() => s_emptyObject;

    private void FaultAllPending(Exception exception)
    {
        foreach (var kvp in _pending)
        {
            if (_pending.TryRemove(kvp.Key, out var tcs))
                tcs.TrySetException(exception);
        }
    }

    private void CompleteAllChannels()
    {
        foreach (var kvp in _eventChannels)
        {
            kvp.Value.Writer.TryComplete();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _cts.CancelAsync().ConfigureAwait(false);

        if (_receiveLoop is not null)
        {
            try { await _receiveLoop.ConfigureAwait(false); }
            catch { /* Swallow; loop already handled cleanup */ }
        }

        FaultAllPending(new ObjectDisposedException(nameof(BiDiTransport)));
        CompleteAllChannels();

        await _socket.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
