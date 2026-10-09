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
            var bytes = await page.ScreenshotAsync().ConfigureAwait(false);
            ex.Screenshot = bytes;
            await SaveToDiskAsync(bytes, failure.ScreenshotPath ?? "test-results/failures", page.ContextInternal).ConfigureAwait(false);
        }
        catch
        {
            // Screenshot capture must never mask the original error
        }
    }

    private static async Task SaveToDiskAsync(byte[] bytes, string basePath, BrowserContext context)
    {
        try
        {
            var label = context.FailureArtifactLabel;
            var fileName = FailureArtifactName.Build("failure", label, "png", DateTime.UtcNow);
            var filePath = FailureArtifactName.ReservePath(basePath, fileName);
            await File.WriteAllBytesAsync(filePath, bytes).ConfigureAwait(false);
            context.RecordFailureScreenshot(filePath);
        }
        catch
        {
            // Disk save is best-effort
        }
    }
}
