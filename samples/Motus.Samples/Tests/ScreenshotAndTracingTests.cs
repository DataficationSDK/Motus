using System.IO.Compression;
using System.Text.Json;

namespace Motus.Samples.Tests;

/// <summary>
/// Screenshots (page and element level) and tracing.
/// </summary>
[MotusTestClass]
public class ScreenshotAndTracingTests : MotusTestBase
{
    [TestMethod]
    public async Task ScreenshotAsync_CapturesFullPage()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // Capture via the <body> locator; the clip-based CDP path is reliable
        // across all platforms (Page.captureScreenshot without Clip can hang)
        var body = Page.Locator("body");
        var bytes = await body.ScreenshotAsync();

        Assert.IsTrue(bytes.Length > 0, "Screenshot should produce non-empty bytes");
    }

    [TestMethod]
    public async Task LocatorScreenshot_CapturesElement()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // Element-level screenshot clips to the element's bounding box
        var card = Page.GetByTestId("card-revenue");
        var bytes = await card.ScreenshotAsync();

        Assert.IsTrue(bytes.Length > 0, "Element screenshot should produce non-empty bytes");
    }

    [TestMethod]
    public async Task Tracing_ProducesZipFile()
    {
        var tracePath = Path.Combine(Path.GetTempPath(), $"motus-trace-{Guid.NewGuid()}.zip");

        try
        {
            await Context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Name = "sample-trace"
            });

            await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);
            await Page.Locator("#toggle-sidebar").ClickAsync();

            await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });

            Assert.IsTrue(File.Exists(tracePath), "Trace zip should be created on disk");

            // An empty recording still packages into a well formed archive, so the events
            // inside the ZIP are what says the trace is usable.
            using var zip = ZipFile.OpenRead(tracePath);
            var entry = zip.GetEntry("trace.json");
            Assert.IsNotNull(entry, "Trace zip should contain trace.json");

            await using var stream = entry!.Open();
            var events = await JsonSerializer.DeserializeAsync<List<JsonElement>>(stream);
            Assert.IsNotNull(events);
            Assert.IsTrue(events!.Count > 0, "trace.json should hold the events the browser recorded");
        }
        finally
        {
            if (File.Exists(tracePath))
                File.Delete(tracePath);
        }
    }
}
