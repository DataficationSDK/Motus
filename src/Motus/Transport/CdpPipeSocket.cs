using System.Buffers;

namespace Motus;

/// <summary>
/// Carries CDP over a pair of pipes to a browser this process started, in place of a WebSocket.
/// </summary>
/// <remarks>
/// A browser started with <c>--remote-debugging-pipe</c> reads commands on one descriptor and
/// writes everything it has to say on another. Each message is a JSON document followed by a
/// single NUL byte, in both directions, and nothing else marks where one ends: a read routinely
/// stops in the middle of a document, and one read routinely carries several.
///
/// The pipe earns its keep in what it does when this process dies. A port carries no liveness
/// signal, so a browser reached over one keeps running after its launcher is gone, however the
/// launcher went. A browser reached over a pipe sees its end close and exits on its own, which
/// covers the one case no signal handler can: being killed outright.
/// </remarks>
internal sealed class CdpPipeSocket : ICdpSocket
{
    /// <summary>The byte that ends every message, in both directions.</summary>
    private const byte MessageTerminator = 0;

    private const int InitialBufferSize = 32 * 1024;

    private static readonly ReadOnlyMemory<byte> Terminator = new byte[] { MessageTerminator };

    private readonly Stream _toBrowser;
    private readonly Stream _fromBrowser;
    private readonly byte[] _readChunk = new byte[InitialBufferSize];

    // Bytes read past the end of the last complete message. A read is sized for throughput rather
    // than for message boundaries, so it usually ends part way through a document and usually
    // carries the beginning of the next one.
    private byte[] _pending = ArrayPool<byte>.Shared.Rent(InitialBufferSize);
    private int _pendingLength;

    // The message handed to the last caller. Kept whole rather than sliced out of the buffer
    // above, so that shifting the leftovers along cannot rewrite a message somebody is reading.
    private byte[] _message = ArrayPool<byte>.Shared.Rent(InitialBufferSize);

    private bool _disposed;
    private bool _ended;

    internal CdpPipeSocket(Stream toBrowser, Stream fromBrowser)
    {
        _toBrowser = toBrowser;
        _fromBrowser = fromBrowser;
    }

    public bool IsOpen => !_disposed && !_ended;

    /// <summary>
    /// Does nothing. The pipes were opened when the browser was started and there is no endpoint
    /// to dial; this exists because the transport is written against one socket abstraction.
    /// </summary>
    public Task ConnectAsync(Uri endpointUri, CancellationToken ct) => Task.CompletedTask;

    public async Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _toBrowser.WriteAsync(message, ct).ConfigureAwait(false);
        await _toBrowser.WriteAsync(Terminator, ct).ConfigureAwait(false);
        await _toBrowser.FlushAsync(ct).ConfigureAwait(false);
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Everything up to here has already been looked at and holds no terminator, so a read that
        // adds to the buffer only needs its own bytes scanned.
        var scanned = 0;

        while (true)
        {
            var found = _pending.AsSpan(scanned, _pendingLength - scanned).IndexOf(MessageTerminator);
            if (found >= 0)
                return TakeMessage(scanned + found);

            scanned = _pendingLength;

            var read = await _fromBrowser.ReadAsync(_readChunk, ct).ConfigureAwait(false);
            if (read == 0)
            {
                // The browser closed its end, which is how it says it has gone.
                _ended = true;
                return ReadOnlyMemory<byte>.Empty;
            }

            Append(_readChunk.AsSpan(0, read));
        }
    }

    /// <summary>
    /// Lifts the message ending at <paramref name="terminator"/> out of the buffer and shuffles
    /// whatever followed it to the front.
    /// </summary>
    private ReadOnlyMemory<byte> TakeMessage(int terminator)
    {
        Grow(ref _message, terminator, preserve: 0);
        _pending.AsSpan(0, terminator).CopyTo(_message);

        var remaining = _pendingLength - terminator - 1;
        if (remaining > 0)
            _pending.AsSpan(terminator + 1, remaining).CopyTo(_pending);

        _pendingLength = remaining;

        return new ReadOnlyMemory<byte>(_message, 0, terminator);
    }

    private void Append(ReadOnlySpan<byte> bytes)
    {
        Grow(ref _pending, _pendingLength + bytes.Length, preserve: _pendingLength);
        bytes.CopyTo(_pending.AsSpan(_pendingLength));
        _pendingLength += bytes.Length;
    }

    /// <summary>
    /// Makes sure a pooled buffer holds at least <paramref name="required"/> bytes, carrying the
    /// first <paramref name="preserve"/> of them over.
    /// </summary>
    private static void Grow(ref byte[] buffer, int required, int preserve)
    {
        if (buffer.Length >= required)
            return;

        var grown = ArrayPool<byte>.Shared.Rent(Math.Max(buffer.Length * 2, required));

        if (preserve > 0)
            buffer.AsSpan(0, preserve).CopyTo(grown);

        ArrayPool<byte>.Shared.Return(buffer);
        buffer = grown;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        // Closing the write end is what tells the browser to go, so it comes first and is not
        // skipped when the read side objects.
        try { await _toBrowser.DisposeAsync().ConfigureAwait(false); } catch { /* already gone */ }
        try { await _fromBrowser.DisposeAsync().ConfigureAwait(false); } catch { /* already gone */ }

        ArrayPool<byte>.Shared.Return(_pending);
        ArrayPool<byte>.Shared.Return(_message);
    }
}
