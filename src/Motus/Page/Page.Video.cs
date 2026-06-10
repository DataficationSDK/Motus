using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    public async Task<string> StartVideoRecordingAsync(
        string? path = null, ViewportSize? size = null, CancellationToken ct = default)
    {
        if (_videoRecorder is not null)
        {
            throw new InvalidOperationException(_videoRecorder.OwnedByContext
                ? "Video recording was enabled for this page's context at creation; it records the "
                  + "whole page life and finalizes when the page closes. Use Video to access it."
                : "Video recording is already in progress on this page.");
        }

        // Capture at the actual viewport so the footage matches what screenshots
        // and coordinates refer to; fall back to a sane size when no viewport
        // emulation is active.
        var resolvedSize = size ?? _viewportSize ?? new ViewportSize(1280, 800);
        var resolvedPath = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(Path.GetTempPath(), $"video-{Guid.NewGuid():N}.avi")
            : path;

        var recorder = new VideoRecorder(this, resolvedPath, resolvedSize.Width, resolvedSize.Height);
        await recorder.StartAsync(ct).ConfigureAwait(false);
        _videoRecorder = recorder;
        return resolvedPath;
    }

    public async Task<string> StopVideoRecordingAsync(CancellationToken ct = default)
    {
        var recorder = _videoRecorder
            ?? throw new InvalidOperationException("No video recording is in progress on this page.");

        if (recorder.OwnedByContext)
        {
            throw new InvalidOperationException(
                "Video recording was enabled for this page's context at creation; it records the "
                + "whole page life and finalizes when the page closes. Use Video to access it.");
        }

        await recorder.StopAndFinalizeAsync().ConfigureAwait(false);
        _videoRecorder = null;
        return recorder.OutputPath;
    }
}
