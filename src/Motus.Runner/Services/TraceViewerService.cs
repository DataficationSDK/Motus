using System.IO.Compression;
using System.Text.Json;
using Motus.Runner.Services.Timeline;

namespace Motus.Runner.Services;

/// <summary>
/// Loads a trace ZIP and populates the runner timeline UI with recorded data.
/// </summary>
public sealed class TraceViewerService
{
    private readonly ITimelineService _timeline;

    public TraceViewerService(ITimelineService timeline)
    {
        _timeline = timeline;
    }

    public async Task LoadFromFileAsync(string zipPath)
    {
        using var fileStream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

        // Load trace.json
        var traceEntry = archive.GetEntry("trace.json");
        List<JsonElement>? traceEvents = null;
        if (traceEntry is not null)
        {
            await using var stream = traceEntry.Open();
            traceEvents = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream).ConfigureAwait(false);
        }

        // Load screenshots
        var screenshots = new List<(int Seq, byte[] Data)>();
        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.StartsWith("resources/screenshots/", StringComparison.Ordinal)
                && entry.FullName.EndsWith(".jpeg", StringComparison.Ordinal))
            {
                await using var stream = entry.Open();
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms).ConfigureAwait(false);
                var seqStr = Path.GetFileNameWithoutExtension(entry.Name);
                if (int.TryParse(seqStr, out var seq))
                    screenshots.Add((seq, ms.ToArray()));
            }
        }

        screenshots.Sort((a, b) => a.Seq.CompareTo(b.Seq));

        // Synthesize timeline entries from trace events
        if (traceEvents is not null)
        {
            int index = 0;
            foreach (var evt in traceEvents)
            {
                if (evt.ValueKind != JsonValueKind.Object)
                    continue;

                var name = evt.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
                var cat = evt.TryGetProperty("cat", out var catProp) ? catProp.GetString() : null;

                if (name is null) continue;

                // Only surface user-visible trace events
                if (cat == "devtools.timeline" || cat == "disabled-by-default-devtools.screenshot")
                {
                    byte[]? screenshotData = null;
                    if (index < screenshots.Count)
                        screenshotData = screenshots[index].Data;

                    var entry = new TimelineEntry(
                        Index: index,
                        Timestamp: DateTime.UtcNow,
                        ActionType: name,
                        Selector: cat ?? "",
                        Duration: TimeSpan.Zero,
                        ScreenshotBefore: screenshotData,
                        ScreenshotAfter: null,
                        HasError: false,
                        ErrorMessage: null,
                        NetworkRequests: Array.Empty<NetworkCapture>(),
                        ConsoleMessages: Array.Empty<ConsoleCapture>(),
                        TestName: "Trace Playback");

                    _timeline.AddEntry(entry);
                    index++;
                }
            }
        }
    }
}
