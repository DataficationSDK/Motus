using System.Text;

namespace Motus.Tests.Transport;

/// <summary>
/// How a pipe to the browser finds message boundaries, given that a read knows nothing about them.
/// </summary>
[TestClass]
public class CdpPipeSocketTests
{
    [TestMethod]
    public async Task Receive_ReadsOneMessagePerTerminator()
    {
        await using var socket = SocketOver("{\"id\":1}\0{\"id\":2}\0");

        Assert.AreEqual("{\"id\":1}", await ReadAsync(socket));
        Assert.AreEqual("{\"id\":2}", await ReadAsync(socket));
    }

    [TestMethod]
    public async Task Receive_JoinsAMessageSplitAcrossReads()
    {
        // The terminator lands in the third read, so the first two carry no complete message.
        await using var socket = SocketOver("{\"id\"", ":1,\"result\"", ":{}}\0");

        Assert.AreEqual("{\"id\":1,\"result\":{}}", await ReadAsync(socket));
    }

    [TestMethod]
    public async Task Receive_KeepsWhatFollowsATerminatorForTheNextRead()
    {
        // One read ends part way through the second message, which is the ordinary case: the
        // leftovers have to survive being handed the first one.
        await using var socket = SocketOver("{\"a\":1}\0{\"b\":", "2}\0");

        Assert.AreEqual("{\"a\":1}", await ReadAsync(socket));
        Assert.AreEqual("{\"b\":2}", await ReadAsync(socket));
    }

    [TestMethod]
    public async Task Receive_HandlesAMessageLargerThanOneRead()
    {
        var payload = new string('x', 200_000);
        await using var socket = SocketOver($"\"{payload}\"\0");

        Assert.AreEqual($"\"{payload}\"", await ReadAsync(socket));
    }

    [TestMethod]
    public async Task Receive_ReportsTheClosedPipeAsAnEmptyMessage()
    {
        await using var socket = SocketOver("{\"id\":1}\0");

        Assert.AreEqual("{\"id\":1}", await ReadAsync(socket));

        var closed = await socket.ReceiveAsync(CancellationToken.None);
        Assert.IsTrue(closed.IsEmpty, "a browser that closed its end reads as an empty message");
        Assert.IsFalse(socket.IsOpen);
    }

    [TestMethod]
    public async Task Send_TerminatesEveryMessage()
    {
        var written = new MemoryStream();
        await using var socket = new CdpPipeSocket(written, new ChunkedStream([]));

        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"id\":1}"), CancellationToken.None);
        await socket.SendAsync(Encoding.UTF8.GetBytes("{\"id\":2}"), CancellationToken.None);

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes("{\"id\":1}\0{\"id\":2}\0"),
            written.ToArray());
    }

    private static CdpPipeSocket SocketOver(params string[] chunks)
        => new(new MemoryStream(), new ChunkedStream(chunks.Select(Encoding.UTF8.GetBytes).ToArray()));

    private static async Task<string> ReadAsync(CdpPipeSocket socket)
        => Encoding.UTF8.GetString((await socket.ReceiveAsync(CancellationToken.None)).Span);

    /// <summary>
    /// A read side that hands back exactly the chunks it was given, one per read, so a test can
    /// decide where a read ends rather than leaving it to the operating system.
    /// </summary>
    private sealed class ChunkedStream(byte[][] chunks) : Stream
    {
        private int _next;
        private int _taken;

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            while (_next < chunks.Length && _taken >= chunks[_next].Length)
            {
                _next++;
                _taken = 0;
            }

            if (_next >= chunks.Length)
                return 0;

            // A chunk longer than the caller's buffer is handed over across several reads, which is
            // what a real pipe does with a message bigger than the reader's buffer.
            var chunk = chunks[_next];
            var count = Math.Min(buffer.Length, chunk.Length - _taken);
            chunk.AsSpan(_taken, count).CopyTo(buffer);
            _taken += count;
            return count;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            => ValueTask.FromResult(Read(buffer.Span));

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
