using System.IO.Compression;
using System.Text.Json;
using Motus.Abstractions;
using Motus.Assertions;
using Motus.Runner.Services;
using Motus.Runner.Services.Timeline;

namespace Motus.Tests.Context;

/// <summary>
/// End to end cover for tracing against a real browser. The fake transport can only prove that
/// Motus handles a payload shaped the way the test author expected; only a real browser proves
/// the shape was right in the first place.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class TracingIntegrationTests
{
    private const string TestPage =
        "data:text/html,<html><body><button id='go' onclick=\"document.getElementById('out').textContent='clicked'\">Go</button><p id='out'>idle</p></body></html>";

    private IBrowser? _browser;
    private string _tracePath = "";

    [TestInitialize]
    public async Task Setup()
    {
        _tracePath = Path.Combine(Path.GetTempPath(), $"motus-trace-integration-{Guid.NewGuid()}.zip");

        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();

        if (File.Exists(_tracePath))
            File.Delete(_tracePath);
    }

    /// <summary>
    /// A trace recorded against a real browser has to contain the browser's timeline events, and
    /// the trace viewer has to turn them into steps.
    /// </summary>
    /// <remarks>
    /// The stop path used to write a well formed ZIP whose trace.json was an empty list, so the
    /// file existed, opened cleanly, and played back as nothing at all. Asserting only that the
    /// ZIP is non-empty cannot tell that apart from a real recording, so this asserts on the
    /// events inside it and on what the viewer makes of them.
    /// </remarks>
    [TestMethod]
    public async Task StopAsync_AfterRealInteraction_WritesTimelineEventsTheViewerCanReplay()
    {
        var context = await _browser!.NewContextAsync();
        var page = await context.NewPageAsync();

        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
        });

        await page.GotoAsync(TestPage);
        await page.Locator("#go").ClickAsync();
        await Expect.That(page.Locator("#out")).ToHaveTextAsync("clicked");

        await context.Tracing.StopAsync(new TracingStopOptions { Path = _tracePath });

        Assert.IsTrue(File.Exists(_tracePath), "Trace ZIP should be created");

        var events = await ReadTraceEventsAsync(_tracePath);
        Assert.IsTrue(events.Count > 0,
            "trace.json held no events at all, so nothing the browser recorded survived the stop.");

        var timelineEvents = events.Count(e => HasCategory(e, "devtools.timeline"));
        Assert.IsTrue(timelineEvents > 0,
            $"trace.json held {events.Count} events but none from the browser timeline, "
            + "so the trace carries no record of what the page did.");

        // What `motus trace show` does with the file. Zero entries is what renders as
        // "No actions recorded".
        var timeline = new TimelineService();
        await new TraceViewerService(timeline).LoadFromFileAsync(_tracePath);

        Assert.IsTrue(timeline.Entries.Count > 0,
            $"The viewer found no actions in a trace of {events.Count} events.");
        Assert.IsTrue(timeline.Entries.Any(e => !string.IsNullOrWhiteSpace(e.ActionType)),
            "Recorded actions should carry a label to display.");

        await context.CloseAsync();
    }

    private static async Task<List<JsonElement>> ReadTraceEventsAsync(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entry = zip.GetEntry("trace.json");
        Assert.IsNotNull(entry, "ZIP should contain trace.json");

        await using var stream = entry!.Open();
        var events = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream);
        Assert.IsNotNull(events);
        return events!;
    }

    /// <summary>
    /// A trace event's category field is a comma separated list, so an event can carry the
    /// category of interest alongside several others.
    /// </summary>
    private static bool HasCategory(JsonElement evt, string category)
    {
        if (evt.ValueKind != JsonValueKind.Object || !evt.TryGetProperty("cat", out var cat))
            return false;

        var value = cat.GetString();
        if (value is null)
            return false;

        return value.Split(',').Any(part => part.Trim() == category);
    }
}
