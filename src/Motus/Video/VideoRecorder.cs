namespace Motus;

/// <summary>
/// Manages the screencast-to-AVI pipeline for video recording.
/// </summary>
internal sealed class VideoRecorder
{
    private readonly Page _page;
    private readonly string _outputPath;
    private readonly int _width;
    private readonly int _height;
    private readonly double _fps;
    private MjpegAviWriter? _writer;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;
    private readonly TaskCompletionSource _completedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Diagnostics.Stopwatch _clock = new();
    private MotusVideo? _video;

    internal VideoRecorder(Page page, string outputPath, int width, int height, double fps = 25)
    {
        _page = page;
        _outputPath = outputPath;
        _width = width;
        _height = height;
        _fps = fps;
    }

    internal MotusVideo Video => _video ??= new MotusVideo(_outputPath, _completedTcs.Task);

    internal string OutputPath => _outputPath;

    /// <summary>
    /// Whether the recorder was started by the owning context's RecordVideo
    /// option, as opposed to an explicit per-page start. Context-owned
    /// recordings span the page's whole life and finalize when it closes.
    /// </summary>
    internal bool OwnedByContext { get; init; }

    internal async Task StartAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var fileStream = new FileStream(_outputPath, FileMode.Create, FileAccess.ReadWrite);
        _writer = new MjpegAviWriter(fileStream, _width, _height, _fps);

        _pumpCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        await _page.StartScreencastAsync("jpeg", quality: 80, maxWidth: _width, maxHeight: _height, ct: ct)
            .ConfigureAwait(false);

        _clock.Start();
        _pumpTask = PumpFramesAsync(_pumpCts.Token);
    }

    internal async Task StopAndFinalizeAsync()
    {
        if (_pumpCts is null) return;

        _pumpCts.Cancel();

        if (_pumpTask is not null)
        {
            try { await _pumpTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _clock.Stop();

        try
        {
            await _page.StopScreencastAsync().ConfigureAwait(false);
        }
        catch { /* session may be gone */ }

        if (_writer is not null)
        {
            // The screencast delivers frames as the page changes, not at a fixed cadence, and the
            // effective rate falls as resolution rises. Stamp the measured rate so the file plays
            // back at real-time instead of the nominal fps. Needs at least two frames to be a rate.
            var seconds = _clock.Elapsed.TotalSeconds;
            if (_writer.FrameCount >= 2 && seconds > 0.1)
                _writer.EffectiveFps = Math.Clamp(_writer.FrameCount / seconds, 1.0, 60.0);

            await _writer.DisposeAsync().ConfigureAwait(false);
        }

        _completedTcs.TrySetResult();
    }

    private async Task PumpFramesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _page.SubscribeScreencastFramesAsync(ct).ConfigureAwait(false))
            {
                var jpegBytes = Convert.FromBase64String(frame.Data);
                await _writer!.AddFrameAsync(jpegBytes).ConfigureAwait(false);

                try
                {
                    await _page.AckScreencastFrameAsync(frame.SessionId, ct).ConfigureAwait(false);
                }
                catch { /* ack failure is non-fatal */ }
            }
        }
        catch (OperationCanceledException) { }
    }
}
