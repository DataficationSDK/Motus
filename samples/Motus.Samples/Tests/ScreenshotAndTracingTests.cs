namespace Motus.Samples.Tests;

/// <summary>
/// Phase 2E/3D: Screenshots (page and element level) and tracing.
/// </summary>
[TestClass]
public class ScreenshotAndTracingTests : MotusTestBase
{
    [TestMethod]
    public async Task ScreenshotAsync_CapturesFullPage()
    {
        await Page.SetContentAsync(Fixtures.Dashboard);

        // FullPage captures the entire scrollable area, not just the viewport
        var bytes = await Page.ScreenshotAsync(new ScreenshotOptions { FullPage = true });

        Assert.IsTrue(bytes.Length > 0, "Screenshot should produce non-empty bytes");
    }

    [TestMethod]
    public async Task LocatorScreenshot_CapturesElement()
    {
        await Page.SetContentAsync(Fixtures.Dashboard);

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
            // Start tracing with screenshots and DOM snapshots
            await Context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Name = "sample-trace"
            });

            await Page.SetContentAsync(Fixtures.Dashboard);
            await Page.Locator("#toggle-sidebar").ClickAsync();

            // Stop tracing and export to a zip file
            await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });

            Assert.IsTrue(File.Exists(tracePath), "Trace zip should be created on disk");
            Assert.IsTrue(new FileInfo(tracePath).Length > 0, "Trace zip should be non-empty");
        }
        finally
        {
            if (File.Exists(tracePath))
                File.Delete(tracePath);
        }
    }
}
