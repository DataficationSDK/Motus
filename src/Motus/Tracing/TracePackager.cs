using System.IO.Compression;
using System.Text.Json;

namespace Motus;

/// <summary>
/// Writes a structured trace ZIP containing trace events, HAR data, and screenshots.
/// </summary>
internal static class TracePackager
{
    internal static async Task WriteAsync(
        string outputPath,
        IReadOnlyList<JsonElement> traceEvents,
        HarLog? harLog,
        IReadOnlyList<ScreenshotEntry>? screenshots)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

        // trace.json
        var traceEntry = archive.CreateEntry("trace.json", CompressionLevel.Optimal);
        await using (var traceStream = traceEntry.Open())
        {
            await JsonSerializer.SerializeAsync(traceStream, traceEvents).ConfigureAwait(false);
        }

        // har.json
        if (harLog is not null)
        {
            var harEntry = archive.CreateEntry("har.json", CompressionLevel.Optimal);
            await using var harStream = harEntry.Open();
            await JsonSerializer.SerializeAsync(
                harStream,
                harLog,
                HarJsonContext.Default.HarLog).ConfigureAwait(false);
        }

        // screenshots
        if (screenshots is { Count: > 0 })
        {
            foreach (var shot in screenshots)
            {
                var name = $"resources/screenshots/{shot.Seq:D6}.jpeg";
                var shotEntry = archive.CreateEntry(name, CompressionLevel.NoCompression);
                await using var shotStream = shotEntry.Open();
                await shotStream.WriteAsync(shot.JpegData).ConfigureAwait(false);
            }
        }
    }
}
