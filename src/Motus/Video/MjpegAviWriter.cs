using System.Buffers.Binary;

namespace Motus;

/// <summary>
/// Pure C# RIFF AVI writer that wraps raw JPEG frames in an MJPEG AVI container.
/// Structure: RIFF 'AVI ' -> LIST 'hdrl' (avih + strl(strh + strf)) -> LIST 'movi' (00dc chunks) -> idx1
/// </summary>
internal sealed class MjpegAviWriter : IAsyncDisposable
{
    private readonly Stream _output;
    private readonly int _width;
    private readonly int _height;
    private readonly double _fps;
    private readonly List<(long Offset, int Size)> _frameIndex = [];
    private long _moviStartOffset;
    private bool _finalized;

    internal MjpegAviWriter(Stream output, int width, int height, double fps)
    {
        _output = output;
        _width = width;
        _height = height;
        _fps = fps > 0 ? fps : 25;
    }

    internal int FrameCount => _frameIndex.Count;

    internal async Task AddFrameAsync(byte[] jpegBytes)
    {
        if (_frameIndex.Count == 0)
            await WriteHeadersAsync().ConfigureAwait(false);

        var chunkOffset = _output.Position - _moviStartOffset - 4; // offset relative to movi list start (after 'movi')
        await WriteFourCcAsync("00dc").ConfigureAwait(false);
        await WriteUInt32Async((uint)jpegBytes.Length).ConfigureAwait(false);
        await _output.WriteAsync(jpegBytes).ConfigureAwait(false);

        // Pad to even boundary
        if (jpegBytes.Length % 2 != 0)
            _output.WriteByte(0);

        _frameIndex.Add((chunkOffset, jpegBytes.Length));
    }

    internal async Task FinalizeAsync()
    {
        if (_finalized) return;
        _finalized = true;

        if (_frameIndex.Count == 0)
        {
            await WriteHeadersAsync().ConfigureAwait(false);
        }

        // Write idx1 chunk
        var idx1Start = _output.Position;
        await WriteFourCcAsync("idx1").ConfigureAwait(false);
        await WriteUInt32Async((uint)(_frameIndex.Count * 16)).ConfigureAwait(false);

        foreach (var (offset, size) in _frameIndex)
        {
            await WriteFourCcAsync("00dc").ConfigureAwait(false);
            await WriteUInt32Async(0x10).ConfigureAwait(false); // AVIIF_KEYFRAME
            await WriteUInt32Async((uint)offset).ConfigureAwait(false);
            await WriteUInt32Async((uint)size).ConfigureAwait(false);
        }

        // Patch RIFF file size (total file size - 8)
        var totalFileSize = _output.Position;
        _output.Seek(4, SeekOrigin.Begin);
        await WriteUInt32Async((uint)(totalFileSize - 8)).ConfigureAwait(false);

        // Patch avih total frames
        _output.Seek(48, SeekOrigin.Begin); // offset of dwTotalFrames in avih
        await WriteUInt32Async((uint)_frameIndex.Count).ConfigureAwait(false);

        // Patch movi LIST size
        var moviSize = idx1Start - _moviStartOffset;
        _output.Seek(_moviStartOffset - 4, SeekOrigin.Begin);
        await WriteUInt32Async((uint)moviSize).ConfigureAwait(false);

        _output.Seek(0, SeekOrigin.End);
        await _output.FlushAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_finalized)
            await FinalizeAsync().ConfigureAwait(false);
    }

    private async Task WriteHeadersAsync()
    {
        var microSecPerFrame = (uint)(1_000_000.0 / _fps);

        // RIFF header (size placeholder = 0)
        await WriteFourCcAsync("RIFF").ConfigureAwait(false);
        await WriteUInt32Async(0).ConfigureAwait(false); // placeholder
        await WriteFourCcAsync("AVI ").ConfigureAwait(false);

        // LIST 'hdrl'
        var hdrlSizePos = await WriteListHeaderAsync("hdrl").ConfigureAwait(false);

        // avih (main AVI header) - 56 bytes
        await WriteFourCcAsync("avih").ConfigureAwait(false);
        await WriteUInt32Async(56).ConfigureAwait(false);
        await WriteUInt32Async(microSecPerFrame).ConfigureAwait(false); // dwMicroSecPerFrame
        await WriteUInt32Async(0).ConfigureAwait(false); // dwMaxBytesPerSec
        await WriteUInt32Async(0).ConfigureAwait(false); // dwPaddingGranularity
        await WriteUInt32Async(0x10).ConfigureAwait(false); // dwFlags (AVIF_HASINDEX)
        await WriteUInt32Async(0).ConfigureAwait(false); // dwTotalFrames (patched later)
        await WriteUInt32Async(0).ConfigureAwait(false); // dwInitialFrames
        await WriteUInt32Async(1).ConfigureAwait(false); // dwStreams
        await WriteUInt32Async(0).ConfigureAwait(false); // dwSuggestedBufferSize
        await WriteUInt32Async((uint)_width).ConfigureAwait(false); // dwWidth
        await WriteUInt32Async((uint)_height).ConfigureAwait(false); // dwHeight
        await WriteUInt32Async(0).ConfigureAwait(false); // dwReserved[0]
        await WriteUInt32Async(0).ConfigureAwait(false); // dwReserved[1]
        await WriteUInt32Async(0).ConfigureAwait(false); // dwReserved[2]
        await WriteUInt32Async(0).ConfigureAwait(false); // dwReserved[3]

        // LIST 'strl'
        var strlSizePos = await WriteListHeaderAsync("strl").ConfigureAwait(false);

        // strh (stream header) - 56 bytes
        await WriteFourCcAsync("strh").ConfigureAwait(false);
        await WriteUInt32Async(56).ConfigureAwait(false);
        await WriteFourCcAsync("vids").ConfigureAwait(false); // fccType
        await WriteFourCcAsync("MJPG").ConfigureAwait(false); // fccHandler
        await WriteUInt32Async(0).ConfigureAwait(false); // dwFlags
        await WriteUInt16Async(0).ConfigureAwait(false); // wPriority
        await WriteUInt16Async(0).ConfigureAwait(false); // wLanguage
        await WriteUInt32Async(0).ConfigureAwait(false); // dwInitialFrames
        await WriteUInt32Async(1).ConfigureAwait(false); // dwScale
        await WriteUInt32Async((uint)_fps).ConfigureAwait(false); // dwRate
        await WriteUInt32Async(0).ConfigureAwait(false); // dwStart
        await WriteUInt32Async(0).ConfigureAwait(false); // dwLength (patched: not critical)
        await WriteUInt32Async(0).ConfigureAwait(false); // dwSuggestedBufferSize
        await WriteUInt32Async(0).ConfigureAwait(false); // dwQuality
        await WriteUInt32Async(0).ConfigureAwait(false); // dwSampleSize
        await WriteUInt16Async(0).ConfigureAwait(false); // rcFrame left
        await WriteUInt16Async(0).ConfigureAwait(false); // rcFrame top
        await WriteUInt16Async((ushort)_width).ConfigureAwait(false); // rcFrame right
        await WriteUInt16Async((ushort)_height).ConfigureAwait(false); // rcFrame bottom

        // strf (stream format - BITMAPINFOHEADER) - 40 bytes
        await WriteFourCcAsync("strf").ConfigureAwait(false);
        await WriteUInt32Async(40).ConfigureAwait(false);
        await WriteUInt32Async(40).ConfigureAwait(false); // biSize
        await WriteInt32Async(_width).ConfigureAwait(false); // biWidth
        await WriteInt32Async(_height).ConfigureAwait(false); // biHeight
        await WriteUInt16Async(1).ConfigureAwait(false); // biPlanes
        await WriteUInt16Async(24).ConfigureAwait(false); // biBitCount
        await WriteFourCcAsync("MJPG").ConfigureAwait(false); // biCompression
        await WriteUInt32Async((uint)(_width * _height * 3)).ConfigureAwait(false); // biSizeImage
        await WriteUInt32Async(0).ConfigureAwait(false); // biXPelsPerMeter
        await WriteUInt32Async(0).ConfigureAwait(false); // biYPelsPerMeter
        await WriteUInt32Async(0).ConfigureAwait(false); // biClrUsed
        await WriteUInt32Async(0).ConfigureAwait(false); // biClrImportant

        // Patch strl LIST size
        PatchListSize(strlSizePos);

        // Patch hdrl LIST size
        PatchListSize(hdrlSizePos);

        // LIST 'movi'
        await WriteFourCcAsync("LIST").ConfigureAwait(false);
        await WriteUInt32Async(0).ConfigureAwait(false); // placeholder
        _moviStartOffset = _output.Position;
        await WriteFourCcAsync("movi").ConfigureAwait(false);
    }

    private async Task<long> WriteListHeaderAsync(string listType)
    {
        await WriteFourCcAsync("LIST").ConfigureAwait(false);
        var sizePos = _output.Position;
        await WriteUInt32Async(0).ConfigureAwait(false); // placeholder
        await WriteFourCcAsync(listType).ConfigureAwait(false);
        return sizePos;
    }

    private void PatchListSize(long sizePos)
    {
        var currentPos = _output.Position;
        var size = (uint)(currentPos - sizePos - 4);
        _output.Seek(sizePos, SeekOrigin.Begin);
        Span<byte> buf = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, size);
        _output.Write(buf);
        _output.Seek(currentPos, SeekOrigin.Begin);
    }

    private async Task WriteFourCcAsync(string fourCc)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(fourCc);
        await _output.WriteAsync(bytes.AsMemory(0, 4)).ConfigureAwait(false);
    }

    private async Task WriteUInt32Async(uint value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        await _output.WriteAsync(buf).ConfigureAwait(false);
    }

    private async Task WriteInt32Async(int value)
    {
        var buf = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(buf, value);
        await _output.WriteAsync(buf).ConfigureAwait(false);
    }

    private async Task WriteUInt16Async(ushort value)
    {
        var buf = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buf, value);
        await _output.WriteAsync(buf).ConfigureAwait(false);
    }
}
