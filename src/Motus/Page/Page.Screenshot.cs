using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    public async Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null)
    {
        var format = options?.Type == ScreenshotType.Jpeg ? "jpeg" : "png";
        var quality = options?.Type == ScreenshotType.Jpeg ? options.Quality : null;

        var result = await _session.SendAsync(
            "Page.captureScreenshot",
            new PageCaptureScreenshotParams(
                Format: format,
                Quality: quality,
                CaptureBeyondViewport: options?.FullPage == true ? true : null),
            CdpJsonContext.Default.PageCaptureScreenshotParams,
            CdpJsonContext.Default.PageCaptureScreenshotResult,
            _pageCts.Token).ConfigureAwait(false);

        var bytes = Convert.FromBase64String(result.Data);

        if (options?.Path is not null)
        {
            var dir = Path.GetDirectoryName(options.Path);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(options.Path, bytes, _pageCts.Token).ConfigureAwait(false);
        }

        return bytes;
    }

    public async Task<byte[]> PdfAsync(string? path = null)
    {
        var result = await _session.SendAsync(
            "Page.printToPDF",
            CdpJsonContext.Default.PagePrintToPdfResult,
            _pageCts.Token).ConfigureAwait(false);

        var bytes = Convert.FromBase64String(result.Data);

        if (path is not null)
        {
            var dir = Path.GetDirectoryName(path);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(path, bytes, _pageCts.Token).ConfigureAwait(false);
        }

        return bytes;
    }
}
