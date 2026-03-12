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
