using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    public async Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null)
    {
        var format = options?.Type == ScreenshotType.Jpeg ? "jpeg" : "png";
        var quality = options?.Type == ScreenshotType.Jpeg ? options.Quality : null;
        bool? beyondViewport = options?.FullPage == true ? true : null;

        // A clip travels in its own request shape rather than as one more field beside the others,
        // so it needs the params record that carries one. Sending the plain record with a clip set
        // drops the region silently and answers with the whole viewport, which reads as the clip
        // having been ignored rather than refused.
        string data;
        if (options?.Clip is { } clip)
        {
            var clipped = await _session.SendAsync(
                "Page.captureScreenshot",
                new PageCaptureScreenshotWithClipParams(
                    Clip: new PageClipRect(clip.X, clip.Y, clip.Width, clip.Height),
                    Format: format,
                    Quality: quality,
                    CaptureBeyondViewport: beyondViewport),
                CdpJsonContext.Default.PageCaptureScreenshotWithClipParams,
                CdpJsonContext.Default.PageCaptureScreenshotResult,
                _pageCts.Token).ConfigureAwait(false);

            data = clipped.Data;
        }
        else
        {
            var whole = await _session.SendAsync(
                "Page.captureScreenshot",
                new PageCaptureScreenshotParams(
                    Format: format,
                    Quality: quality,
                    CaptureBeyondViewport: beyondViewport),
                CdpJsonContext.Default.PageCaptureScreenshotParams,
                CdpJsonContext.Default.PageCaptureScreenshotResult,
                _pageCts.Token).ConfigureAwait(false);

            data = whole.Data;
        }

        var bytes = Convert.FromBase64String(data);

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
