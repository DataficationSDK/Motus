namespace Motus.Samples.Tests;

/// <summary>
/// Live web tests that navigate to real URLs.
/// Tagged with TestCategory("Integration") so CI can run them separately.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class LiveWebTests : MotusTestBase
{
    [TestMethod]
    public async Task ExampleDotCom_HasExpectedTitle()
    {
        await Page.GotoAsync("https://example.com");

        await Expect.That(Page).ToHaveTitleAsync("Example Domain");
        await Expect.That(Page.GetByText("Example Domain")).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task ExampleDotCom_FullPageScreenshot()
    {
        await Page.GotoAsync("https://example.com");

        var screenshotPath = Path.Combine(Path.GetTempPath(), $"motus-live-{Guid.NewGuid()}.png");
        try
        {
            var bytes = await Page.ScreenshotAsync(new ScreenshotOptions
            {
                FullPage = true,
                Path = screenshotPath
            });

            Assert.IsTrue(bytes.Length > 0, "Screenshot bytes should not be empty");
            Assert.IsTrue(File.Exists(screenshotPath), "Screenshot file should be written to disk");
        }
        finally
        {
            if (File.Exists(screenshotPath))
                File.Delete(screenshotPath);
        }
    }
}
