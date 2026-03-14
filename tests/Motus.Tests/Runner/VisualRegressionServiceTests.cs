using Motus.Runner.Services.VisualRegression;
using SkiaSharp;

namespace Motus.Tests.Runner;

[TestClass]
public class VisualRegressionServiceTests
{
    [TestMethod]
    public void IdenticalImages_ZeroDiff()
    {
        var png = CreateSolidPng(10, 10, SKColors.Red);
        var result = VisualRegressionService.PixelDiff(png, png);

        Assert.IsTrue(result.IsMatch);
        Assert.AreEqual(0, result.DiffPercent);
        Assert.AreEqual(0, result.DiffPixelCount);
    }

    [TestMethod]
    public void DifferentImages_HighDiff()
    {
        var red = CreateSolidPng(10, 10, SKColors.Red);
        var blue = CreateSolidPng(10, 10, SKColors.Blue);

        var result = VisualRegressionService.PixelDiff(red, blue);

        Assert.IsFalse(result.IsMatch);
        Assert.IsTrue(result.DiffPercent > 50);
        Assert.AreEqual(100, result.DiffPixelCount);
        Assert.IsNotNull(result.DiffImage);
    }

    [TestMethod]
    public void MismatchedSizes_100Percent()
    {
        var small = CreateSolidPng(5, 5, SKColors.Red);
        var large = CreateSolidPng(10, 10, SKColors.Red);

        var result = VisualRegressionService.PixelDiff(small, large);

        Assert.IsFalse(result.IsMatch);
        Assert.AreEqual(100, result.DiffPercent);
    }

    [TestMethod]
    public async Task AcceptBaseline_WritesFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "motus-test-baselines-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new Motus.Runner.RunnerOptions { BaselinePath = tempDir };
            var svc = new VisualRegressionService(options);
            var png = CreateSolidPng(4, 4, SKColors.Green);

            await svc.AcceptBaselineAsync("TestClass.TestMethod", "step1", png);

            var expectedPath = Path.Combine(tempDir, "TestClass.TestMethod", "step1.png");
            Assert.IsTrue(File.Exists(expectedPath));

            var written = await File.ReadAllBytesAsync(expectedPath);
            CollectionAssert.AreEqual(png, written);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void SimilarImages_WithinThreshold_Match()
    {
        // Create two images that differ by less than the threshold (10/255)
        var img1 = CreateSolidPng(10, 10, new SKColor(100, 100, 100));
        var img2 = CreateSolidPng(10, 10, new SKColor(105, 105, 105));

        var result = VisualRegressionService.PixelDiff(img1, img2);

        Assert.IsTrue(result.IsMatch);
        Assert.AreEqual(0, result.DiffPixelCount);
    }

    private static byte[] CreateSolidPng(int width, int height, SKColor color)
    {
        using var bmp = new SKBitmap(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                bmp.SetPixel(x, y, color);

        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
