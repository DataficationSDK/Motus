using Motus.Abstractions;

namespace Motus.Tests.Page;

[TestClass]
[TestCategory("Integration")]
public class PageIntegrationTests
{
    private IBrowser? _browser;

    [TestInitialize]
    public async Task Setup()
    {
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
    }

    /// <summary>
    /// A screenshot clip has to reach the browser rather than be accepted and dropped.
    /// </summary>
    /// <remarks>
    /// A clip travels in a different request shape from a plain capture, so sending the plain one
    /// with a clip set produced a full-viewport image and no error at all. The captured size is
    /// what shows the difference: a caller asking for a small region got back the whole viewport
    /// and nothing said so.
    /// </remarks>
    [TestMethod]
    public async Task ScreenshotAsync_WithAClip_CapturesOnlyThatRegion()
    {
        var page = await _browser!.NewPageAsync();
        await page.SetViewportSizeAsync(new ViewportSize(800, 600));
        await page.GotoAsync("data:text/html,<html><body style='margin:0'><div style='width:800px;height:600px;background:linear-gradient(red,blue)'></div></body></html>");

        var clipped = await page.ScreenshotAsync(new ScreenshotOptions
        {
            Clip = new ClipRect(10, 20, 120, 90)
        });
        var whole = await page.ScreenshotAsync();

        var (clipWidth, clipHeight) = ReadPngSize(clipped);
        var (wholeWidth, wholeHeight) = ReadPngSize(whole);

        Assert.AreEqual(120, clipWidth, "The clip's width did not reach the browser.");
        Assert.AreEqual(90, clipHeight, "The clip's height did not reach the browser.");
        Assert.IsTrue(wholeWidth > clipWidth && wholeHeight > clipHeight,
            $"An unclipped capture ({wholeWidth}x{wholeHeight}) should be larger than a clipped one.");

        await page.DisposeAsync();
    }

    /// <summary>
    /// Reads the pixel dimensions out of a PNG's IHDR chunk, which starts at a fixed offset.
    /// </summary>
    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        Assert.IsTrue(png.Length > 24, "The capture is too short to be a PNG.");

        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (width, height);
    }

    [TestMethod]
    public async Task NewPageAsync_CreatesPage()
    {
        var page = await _browser!.NewPageAsync();
        Assert.IsNotNull(page);
        Assert.IsFalse(page.IsClosed);
        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task GotoAsync_NavigatesToUrl()
    {
        var page = await _browser!.NewPageAsync();

        await page.GotoAsync("data:text/html,<h1>Hello</h1>");

        var title = await page.TitleAsync();
        Assert.IsNotNull(title);

        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task EvaluateAsync_ReturnsValue()
    {
        var page = await _browser!.NewPageAsync();

        await page.GotoAsync("data:text/html,<h1>Test</h1>");

        var result = await page.EvaluateAsync<int>("1 + 2");
        Assert.AreEqual(3, result);

        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task EvaluateAsync_ReturnsString()
    {
        var page = await _browser!.NewPageAsync();

        await page.GotoAsync("data:text/html,<title>Test Page</title>");

        var title = await page.EvaluateAsync<string>("document.title");
        Assert.AreEqual("Test Page", title);

        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task ScreenshotAsync_ReturnsBytes()
    {
        var page = await _browser!.NewPageAsync();

        await page.GotoAsync("data:text/html,<h1>Screenshot</h1>");

        var bytes = await page.ScreenshotAsync();
        Assert.IsTrue(bytes.Length > 0);

        // PNG magic number
        Assert.AreEqual(0x89, bytes[0]);
        Assert.AreEqual(0x50, bytes[1]);

        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task SetViewportSizeAsync_ChangesViewport()
    {
        var page = await _browser!.NewPageAsync();

        await page.SetViewportSizeAsync(new ViewportSize(800, 600));

        Assert.IsNotNull(page.ViewportSize);
        Assert.AreEqual(800, page.ViewportSize.Width);
        Assert.AreEqual(600, page.ViewportSize.Height);

        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task SetInputFilesAsync_DeferredReadReceivesBytes()
    {
        // The browser reads upload payloads lazily. A change handler that reads
        // the file asynchronously (FileReader, arrayBuffer, or a framework
        // streaming the file to a server) must still receive the bytes after
        // the upload action has returned.
        var page = await _browser!.NewPageAsync();

        var html =
            "<input type=file><div id=out></div>" +
            "<script>" +
            "document.querySelector('input').addEventListener('change', e => {" +
            "  const file = e.target.files[0];" +
            "  setTimeout(async () => {" +
            "    try {" +
            "      const buf = await file.arrayBuffer();" +
            "      document.getElementById('out').textContent = 'len:' + buf.byteLength;" +
            "    } catch (err) {" +
            "      document.getElementById('out').textContent = 'error:' + err.name;" +
            "    }" +
            "  }, 100);" +
            "});" +
            "</script>";
        await page.GotoAsync("data:text/html," + html);

        var payload = new byte[4096];
        new Random(42).NextBytes(payload);
        await page.Locator("input").SetInputFilesAsync(
            [new FilePayload("data.bin", "application/octet-stream", payload)]);

        var text = string.Empty;
        for (var i = 0; i < 50 && string.IsNullOrEmpty(text); i++)
        {
            await Task.Delay(100);
            text = await page.EvaluateAsync<string>("document.getElementById('out').textContent");
        }

        Assert.AreEqual($"len:{payload.Length}", text);

        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task ContentAsync_ReturnsHtml()
    {
        var page = await _browser!.NewPageAsync();

        await page.GotoAsync("data:text/html,<p>Hello</p>");

        var content = await page.ContentAsync();
        Assert.IsTrue(content.Contains("Hello"));

        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task Context_HasPage()
    {
        var page = await _browser!.NewPageAsync();

        Assert.IsNotNull(page.Context);
        Assert.AreEqual(1, page.Context.Pages.Count);

        await page.DisposeAsync();
    }

    [TestMethod]
    public async Task MultiplePages_WorkIndependently()
    {
        var page1 = await _browser!.NewPageAsync();
        var page2 = await _browser!.NewPageAsync();

        await page1.GotoAsync("data:text/html,<title>Page1</title>");
        await page2.GotoAsync("data:text/html,<title>Page2</title>");

        var title1 = await page1.EvaluateAsync<string>("document.title");
        var title2 = await page2.EvaluateAsync<string>("document.title");

        Assert.AreEqual("Page1", title1);
        Assert.AreEqual("Page2", title2);

        await page1.DisposeAsync();
        await page2.DisposeAsync();
    }
}
