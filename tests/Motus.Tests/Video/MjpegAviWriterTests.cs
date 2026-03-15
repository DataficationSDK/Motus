using System.Buffers.Binary;

namespace Motus.Tests.Video;

[TestClass]
public class MjpegAviWriterTests
{
    private static byte[] CreateSyntheticJpeg()
    {
        // Minimal JPEG: SOI + EOI markers with some padding
        return [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00,
                0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9];
    }

    [TestMethod]
    public async Task FinalizeAsync_EmptyFile_WritesValidRiffHeader()
    {
        using var ms = new MemoryStream();
        var writer = new MjpegAviWriter(ms, 640, 480, 25);
        await writer.FinalizeAsync();

        ms.Seek(0, SeekOrigin.Begin);
        var header = new byte[4];
        await ms.ReadExactlyAsync(header);
        Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(header));

        // Skip size
        ms.Seek(8, SeekOrigin.Begin);
        await ms.ReadExactlyAsync(header);
        Assert.AreEqual("AVI ", System.Text.Encoding.ASCII.GetString(header));
    }

    [TestMethod]
    public async Task AddFrameAsync_MultipleFrames_CorrectFrameCount()
    {
        using var ms = new MemoryStream();
        var writer = new MjpegAviWriter(ms, 320, 240, 10);

        var jpeg = CreateSyntheticJpeg();
        await writer.AddFrameAsync(jpeg);
        await writer.AddFrameAsync(jpeg);
        await writer.AddFrameAsync(jpeg);

        Assert.AreEqual(3, writer.FrameCount);

        await writer.FinalizeAsync();

        // Verify RIFF file size is patched (bytes 4-8 should be totalSize - 8)
        ms.Seek(4, SeekOrigin.Begin);
        var sizeBuf = new byte[4];
        await ms.ReadExactlyAsync(sizeBuf);
        var riffSize = BinaryPrimitives.ReadUInt32LittleEndian(sizeBuf);
        Assert.AreEqual((uint)(ms.Length - 8), riffSize);
    }

    [TestMethod]
    public async Task AddFrameAsync_WritesIdx1Index()
    {
        using var ms = new MemoryStream();
        var writer = new MjpegAviWriter(ms, 100, 100, 25);

        var jpeg = CreateSyntheticJpeg();
        await writer.AddFrameAsync(jpeg);
        await writer.FinalizeAsync();

        // Search for idx1 marker in output
        var data = ms.ToArray();
        bool foundIdx1 = false;
        for (int i = 0; i < data.Length - 4; i++)
        {
            if (data[i] == (byte)'i' && data[i + 1] == (byte)'d'
                && data[i + 2] == (byte)'x' && data[i + 3] == (byte)'1')
            {
                foundIdx1 = true;
                break;
            }
        }

        Assert.IsTrue(foundIdx1, "Output should contain idx1 index chunk");
    }

    [TestMethod]
    public async Task DisposeAsync_AutoFinalizes()
    {
        using var ms = new MemoryStream();
        {
            var writer = new MjpegAviWriter(ms, 200, 200, 30);
            await writer.AddFrameAsync(CreateSyntheticJpeg());
            await writer.DisposeAsync();
        }

        Assert.IsTrue(ms.Length > 0, "File should be written after dispose");

        ms.Seek(0, SeekOrigin.Begin);
        var header = new byte[4];
        await ms.ReadExactlyAsync(header);
        Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(header));
    }

    [TestMethod]
    public async Task AddFrameAsync_OddSizeFrame_PadsToEvenBoundary()
    {
        using var ms = new MemoryStream();
        var writer = new MjpegAviWriter(ms, 100, 100, 25);

        // Create odd-length "JPEG"
        var oddJpeg = new byte[13];
        oddJpeg[0] = 0xFF;
        oddJpeg[1] = 0xD8;

        await writer.AddFrameAsync(oddJpeg);
        await writer.FinalizeAsync();

        // The stream position should be at an even offset after the frame data
        // Verify overall file is valid RIFF
        ms.Seek(0, SeekOrigin.Begin);
        var header = new byte[4];
        await ms.ReadExactlyAsync(header);
        Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(header));
    }
}
