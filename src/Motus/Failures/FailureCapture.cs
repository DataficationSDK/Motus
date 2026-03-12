using Motus.Abstractions;

namespace Motus;

internal static class FailureCapture
{
    internal static async Task AttachScreenshotAsync(MotusException ex, Page page)
    {
        if (!MotusConfigLoader.Config.Screenshot)
            return;

        try
        {
            var bytes = await page.ScreenshotAsync();
            ex.Screenshot = bytes;
            await SaveToDiskAsync(bytes, MotusConfigLoader.Config.ScreenshotPath);
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
