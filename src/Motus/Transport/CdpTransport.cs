using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

namespace Motus;

/// <summary>
/// Raw event payload surfaced to event channels before typed deserialization.
/// </summary>
internal readonly record struct RawCdpEvent(JsonElement Params, string? SessionId);

/// <summary>
/// Core CDP WebSocket transport. Manages a single WebSocket connection, a background
/// receive loop, request/response correlation, and event channel dispatch.
/// </summary>
internal sealed class CdpTransport : IAsyncDisposable
{
    private readonly ICdpSocket _socket;
    private readonly TimeSpan _slowMo;
    private readonly CancellationTokenSource _cts = new();
    private Task? _receiveLoop;
    private bool _disposed;

    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, Channel<RawCdpEvent>> _eventChannels = new();
    private int _nextId;

    /// <summary>
    /// Raised when the WebSocket connection is lost. The exception (if any) is provided.
    /// </summary>
    internal event Action<Exception?>? Disconnected;

    internal CdpTransport(ICdpSocket socket, TimeSpan slowMo = default)
    {
        _socket = socket;
        _slowMo = slowMo;
    }

    /// <summary>
    /// Connects to the browser CDP endpoint and starts the background receive loop.
    /// </summary>
    internal async Task ConnectAsync(Uri endpointUri, CancellationToken ct)
    {
        await _socket.ConnectAsync(endpointUri, ct).ConfigureAwait(false);
        _receiveLoop = RunReceiveLoopAsync(_cts.Token);
    }

    /// <summary>
    /// Sends a raw CDP command and awaits the correlated response.
    /// </summary>
    internal async Task<JsonElement> SendRawAsync(
        string method, JsonElement paramsElement, string? sessionId, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            if (_slowMo > TimeSpan.Zero)
                await Task.Delay(_slowMo, ct).ConfigureAwait(false);

            var envelope = new CdpCommandEnvelope(id, method, paramsElement, sessionId);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, CdpJsonContext.Default.CdpCommandEnvelope);
            await _socket.SendAsync(bytes, ct).ConfigureAwait(false);

            // Link caller cancellation to the pending TCS
            await using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
            return await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// Gets or creates an event channel for the given composite key.
    /// The key format is <c>"Domain.eventName|sessionId"</c> (empty string for browser-level).
    /// </summary>
    internal Channel<RawCdpEvent> GetOrCreateEventChannel(string channelKey)
    {
        return _eventChannels.GetOrAdd(channelKey, static _ =>
            Channel.CreateUnbounded<RawCdpEvent>(new UnboundedChannelOptions
            {
                SingleReader = false,
                SingleWriter = true
            }));
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
                    // Clean disconnect
                    FaultAllPending(new CdpDisconnectedException());
                    CompleteAllChannels();
                    Disconnected?.Invoke(null);
                    return;
                }

                try
                {
                    var envelope = JsonSerializer.Deserialize(
                        message.Span,
                        CdpJsonContext.Default.CdpInboundEnvelope);

                    if (envelope is not null)
                        DispatchMessage(envelope);
                }
                catch (JsonException)
                {
                    // Malformed CDP message; skip it.
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown via dispose
        }
        catch (Exception ex)
        {
            // Unexpected disconnect
            FaultAllPending(new CdpDisconnectedException(ex));
            CompleteAllChannels();
            Disconnected?.Invoke(ex);
        }
    }

    private void DispatchMessage(CdpInboundEnvelope envelope)
    {
        if (envelope.Id is not null)
        {
            DispatchResponse(envelope);
        }
        else if (envelope.Method is not null)
        {
            DispatchEvent(envelope);
        }
    }

    private void DispatchResponse(CdpInboundEnvelope envelope)
    {
        if (!_pending.TryRemove(envelope.Id!.Value, out var tcs))
            return;

        if (envelope.Error is not null)
        {
            tcs.TrySetException(new CdpProtocolException(envelope.Error.Code, envelope.Error.Message));
        }
        else
        {
            // Default to an empty JSON object if result is missing
            tcs.TrySetResult(envelope.Result ?? EmptyJsonElement());
        }
    }

    private void DispatchEvent(CdpInboundEnvelope envelope)
    {
        var sessionId = envelope.SessionId ?? string.Empty;
        var channelKey = $"{envelope.Method}|{sessionId}";

        if (_eventChannels.TryGetValue(channelKey, out var channel))
        {
            var rawEvent = new RawCdpEvent(
                envelope.Params ?? default,
                envelope.SessionId);

            channel.Writer.TryWrite(rawEvent);
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

        FaultAllPending(new ObjectDisposedException(nameof(CdpTransport)));
        CompleteAllChannels();

        await _socket.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
