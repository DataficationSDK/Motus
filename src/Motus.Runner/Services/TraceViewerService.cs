using System.IO.Compression;
using System.Text.Json;
using Motus.Runner.Services.Timeline;

namespace Motus.Runner.Services;

/// <summary>
/// Loads a trace ZIP and populates the runner timeline UI with recorded data.
/// Extracts timestamps, durations, screenshots, and HAR network requests from
/// the trace archive to produce rich timeline entries.
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

        // Load har.json
        var harNetworkRequests = await LoadHarEntriesAsync(archive).ConfigureAwait(false);

        // Load screenshots
        var screenshots = await LoadScreenshotsAsync(archive).ConfigureAwait(false);

        if (traceEvents is null)
            return;

        // Separate screenshot events from timeline events
        var timelineEvents = new List<JsonElement>();
        var screenshotEvents = new List<JsonElement>();

        foreach (var evt in traceEvents)
        {
            if (evt.ValueKind != JsonValueKind.Object)
                continue;

            var cat = evt.TryGetProperty("cat", out var catProp) ? catProp.GetString() : null;

            if (cat == "disabled-by-default-devtools.screenshot")
                screenshotEvents.Add(evt);
            else if (cat == "devtools.timeline")
                timelineEvents.Add(evt);
        }

        // Build a lookup from screenshot timestamps to image data.
        // If we have ZIP-bundled screenshots, match them sequentially to screenshot events.
        // If not, extract base64 snapshots from the screenshot trace events themselves.
        var screenshotByTimestamp = new Dictionary<long, byte[]>();
        if (screenshots.Count > 0 && screenshotEvents.Count > 0)
        {
            for (int i = 0; i < screenshotEvents.Count && i < screenshots.Count; i++)
            {
                var ts = GetTimestamp(screenshotEvents[i]);
                if (ts > 0)
                    screenshotByTimestamp[ts] = screenshots[i].Data;
            }
        }
        else
        {
            foreach (var evt in screenshotEvents)
            {
                var ts = GetTimestamp(evt);
                if (ts > 0
                    && evt.TryGetProperty("args", out var args)
                    && args.TryGetProperty("snapshot", out var snapshot))
                {
                    var base64 = snapshot.GetString();
                    if (base64 is not null)
                        screenshotByTimestamp[ts] = Convert.FromBase64String(base64);
                }
            }
        }

        // Sort screenshot timestamps for binary search
        var sortedScreenshotTimestamps = screenshotByTimestamp.Keys.OrderBy(t => t).ToList();

        // Build timeline entries from significant events
        int index = 0;
        foreach (var evt in timelineEvents)
        {
            var name = evt.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
            if (name is null)
                continue;

            // Filter to user-visible events
            if (!IsSignificantEvent(name))
                continue;

            var ts = GetTimestamp(evt);
            var dur = GetDuration(evt);
            var label = GetActionLabel(name, evt);
            var selector = GetEventDetail(name, evt);
            var baseTime = timelineEvents.Count > 0 ? GetTimestamp(timelineEvents[0]) : 0;
            var timestamp = ts > 0 && baseTime > 0
                ? DateTime.UnixEpoch.AddMicroseconds(ts)
                : DateTime.UtcNow;

            // Find the closest screenshot taken before or at this event's timestamp
            byte[]? screenshotBefore = FindClosestScreenshot(sortedScreenshotTimestamps, screenshotByTimestamp, ts);

            // Find screenshot after (closest one after this event ends)
            byte[]? screenshotAfter = FindClosestScreenshotAfter(sortedScreenshotTimestamps, screenshotByTimestamp, ts + (long)(dur * 1_000_000));

            // Match HAR entries that fall within this event's time window
            var networkRequests = MatchHarEntries(harNetworkRequests, ts, dur);

            var entry = new TimelineEntry(
                Index: index,
                Timestamp: timestamp,
                ActionType: label,
                Selector: selector,
                Duration: TimeSpan.FromSeconds(dur),
                ScreenshotBefore: screenshotBefore,
                ScreenshotAfter: screenshotAfter,
                HasError: false,
                ErrorMessage: null,
                NetworkRequests: networkRequests,
                ConsoleMessages: Array.Empty<ConsoleCapture>(),
                TestName: "Trace Playback");

            _timeline.AddEntry(entry);
            index++;
        }
    }

    /// <summary>
    /// Returns true for CDP timeline events that represent meaningful user-visible actions.
    /// Filters out internal bookkeeping events to keep the timeline readable.
    /// </summary>
    private static bool IsSignificantEvent(string name) => name switch
    {
        "NavigationStart" => true,
        "ParseHTML" => true,
        "EvaluateScript" => true,
        "EventDispatch" => true,
        "FunctionCall" => true,
        "XHRReadyStateChange" => true,
        "ResourceSendRequest" => true,
        "ResourceReceiveResponse" => true,
        "Layout" => true,
        "Paint" => true,
        "CompositeLayers" => true,
        "TimerFire" => true,
        "RequestAnimationFrame" => true,
        "UpdateLayoutTree" => true,
        "HitTest" => true,
        _ => false,
    };

    /// <summary>
    /// Returns a human-friendly action label for a CDP event name.
    /// </summary>
    private static string GetActionLabel(string name, JsonElement evt) => name switch
    {
        "NavigationStart" => "Navigate",
        "ParseHTML" => "Parse HTML",
        "EvaluateScript" => "Evaluate Script",
        "EventDispatch" => GetEventDispatchLabel(evt),
        "FunctionCall" => GetFunctionCallLabel(evt),
        "XHRReadyStateChange" => "XHR",
        "ResourceSendRequest" => "Send Request",
        "ResourceReceiveResponse" => "Receive Response",
        "Layout" => "Layout",
        "Paint" => "Paint",
        "CompositeLayers" => "Composite",
        "TimerFire" => "Timer",
        "RequestAnimationFrame" => "Animation Frame",
        "UpdateLayoutTree" => "Style Recalc",
        "HitTest" => "Hit Test",
        _ => name,
    };

    /// <summary>
    /// Extracts contextual detail text for the step detail selector field.
    /// </summary>
    private static string? GetEventDetail(string name, JsonElement evt)
    {
        if (!evt.TryGetProperty("args", out var args))
            return null;

        if (!args.TryGetProperty("data", out var data))
            return null;

        return name switch
        {
            "NavigationStart" when TryGetString(data, "documentLoaderURL", out var url) => url,
            "EvaluateScript" when TryGetString(data, "url", out var url) => TruncateUrl(url),
            "FunctionCall" when TryGetString(data, "functionName", out var fn) => fn,
            "EventDispatch" when TryGetString(data, "type", out var type) => type,
            "XHRReadyStateChange" when TryGetString(data, "url", out var url) => TruncateUrl(url),
            "ResourceSendRequest" when TryGetString(data, "url", out var url) => TruncateUrl(url),
            "ResourceReceiveResponse" when TryGetString(data, "url", out var url) => TruncateUrl(url),
            _ => null,
        };
    }

    private static string GetEventDispatchLabel(JsonElement evt)
    {
        if (evt.TryGetProperty("args", out var args)
            && args.TryGetProperty("data", out var data)
            && TryGetString(data, "type", out var type))
        {
            return $"Event: {type}";
        }
        return "Event";
    }

    private static string GetFunctionCallLabel(JsonElement evt)
    {
        if (evt.TryGetProperty("args", out var args)
            && args.TryGetProperty("data", out var data)
            && TryGetString(data, "functionName", out var fn)
            && !string.IsNullOrEmpty(fn))
        {
            return $"Call: {fn}";
        }
        return "Function Call";
    }

    private static long GetTimestamp(JsonElement evt)
    {
        if (evt.TryGetProperty("ts", out var ts))
        {
            if (ts.ValueKind == JsonValueKind.Number)
                return ts.GetInt64();
        }
        return 0;
    }

    private static double GetDuration(JsonElement evt)
    {
        if (evt.TryGetProperty("dur", out var dur))
        {
            if (dur.ValueKind == JsonValueKind.Number)
                return dur.GetDouble() / 1_000_000.0; // microseconds to seconds
        }
        return 0;
    }

    private static bool TryGetString(JsonElement element, string property, out string value)
    {
        value = "";
        if (element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return !string.IsNullOrEmpty(value);
        }
        return false;
    }

    private static string TruncateUrl(string url)
    {
        if (url.Length <= 100)
            return url;
        return url[..97] + "...";
    }

    private static byte[]? FindClosestScreenshot(
        List<long> sortedTimestamps,
        Dictionary<long, byte[]> screenshotData,
        long targetTimestamp)
    {
        if (sortedTimestamps.Count == 0 || targetTimestamp <= 0)
            return null;

        // Binary search for the largest timestamp <= target
        int lo = 0, hi = sortedTimestamps.Count - 1;
        int bestIdx = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (sortedTimestamps[mid] <= targetTimestamp)
            {
                bestIdx = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        if (bestIdx >= 0)
            return screenshotData[sortedTimestamps[bestIdx]];

        return null;
    }

    private static byte[]? FindClosestScreenshotAfter(
        List<long> sortedTimestamps,
        Dictionary<long, byte[]> screenshotData,
        long targetTimestamp)
    {
        if (sortedTimestamps.Count == 0 || targetTimestamp <= 0)
            return null;

        // Binary search for the smallest timestamp >= target
        int lo = 0, hi = sortedTimestamps.Count - 1;
        int bestIdx = -1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (sortedTimestamps[mid] >= targetTimestamp)
            {
                bestIdx = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        if (bestIdx >= 0)
            return screenshotData[sortedTimestamps[bestIdx]];

        return null;
    }

    private static List<NetworkCapture> MatchHarEntries(
        List<HarNetworkEntry> harEntries,
        long eventTimestampUs,
        double eventDurationSec)
    {
        if (harEntries.Count == 0 || eventTimestampUs <= 0)
            return [];

        var eventStart = DateTimeOffset.UnixEpoch.AddTicks(eventTimestampUs * 10);
        var eventEnd = eventStart.AddSeconds(eventDurationSec > 0 ? eventDurationSec : 0.1);

        var matched = new List<NetworkCapture>();
        foreach (var har in harEntries)
        {
            if (har.StartTime >= eventStart && har.StartTime <= eventEnd)
            {
                matched.Add(new NetworkCapture(har.Url, har.Method, har.Status, har.Status == 0 || har.Status >= 400));
            }
        }

        return matched;
    }

    private static async Task<List<HarNetworkEntry>> LoadHarEntriesAsync(ZipArchive archive)
    {
        var harEntry = archive.GetEntry("har.json");
        if (harEntry is null)
            return [];

        try
        {
            await using var stream = harEntry.Open();
            using var doc = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);

            var entries = new List<HarNetworkEntry>();
            if (doc.RootElement.TryGetProperty("entries", out var entriesArray)
                && entriesArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in entriesArray.EnumerateArray())
                {
                    var method = "";
                    var url = "";
                    var status = 0;
                    DateTimeOffset startTime = default;

                    if (entry.TryGetProperty("startedDateTime", out var startProp))
                    {
                        if (DateTimeOffset.TryParse(startProp.GetString(), out var parsed))
                            startTime = parsed;
                    }

                    if (entry.TryGetProperty("request", out var req))
                    {
                        if (TryGetString(req, "method", out var m)) method = m;
                        if (TryGetString(req, "url", out var u)) url = u;
                    }

                    if (entry.TryGetProperty("response", out var resp)
                        && resp.TryGetProperty("status", out var statusProp)
                        && statusProp.ValueKind == JsonValueKind.Number)
                    {
                        status = statusProp.GetInt32();
                    }

                    entries.Add(new HarNetworkEntry(url, method, status, startTime));
                }
            }

            return entries;
        }
        catch
        {
            return [];
        }
    }

    private static async Task<List<(int Seq, byte[] Data)>> LoadScreenshotsAsync(ZipArchive archive)
    {
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
        return screenshots;
    }

    private sealed record HarNetworkEntry(string Url, string Method, int Status, DateTimeOffset StartTime);
}
