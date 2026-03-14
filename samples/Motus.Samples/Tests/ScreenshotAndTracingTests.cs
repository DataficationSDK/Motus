namespace Motus.Samples.Tests;

/// <summary>
/// Screenshots (page and element level) and tracing.
/// </summary>
[TestClass]
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
    [Ignore("Tracing is not yet implemented (NullTracing stub)")]
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
            Assert.IsTrue(new FileInfo(tracePath).Length > 0, "Trace zip should be non-empty");
        }
        finally
        {
            if (File.Exists(tracePath))
                File.Delete(tracePath);
        }
    }
}
