using Motus.Abstractions;

namespace Motus;

internal static class FailureCapture
{
    internal static async Task AttachScreenshotAsync(MotusException ex, Page page)
    {
        var failure = MotusConfigLoader.Config.Failure;
        if (failure is null || failure.Screenshot is not true)
            return;

        try
        {
            var bytes = await page.ScreenshotAsync();
            ex.Screenshot = bytes;
            await SaveToDiskAsync(bytes, failure.ScreenshotPath ?? "test-results/failures");
        }
        catch
        {
            // Screenshot capture must never mask the original error
        }
    }

    private static async Task SaveToDiskAsync(byte[] bytes, string basePath)
    {
        try
        {
            Directory.CreateDirectory(basePath);
            var fileName = $"failure-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.png";
            var filePath = Path.Combine(basePath, fileName);
            await File.WriteAllBytesAsync(filePath, bytes);
        }
        catch
        {
            // Disk save is best-effort
        }
    }
}
