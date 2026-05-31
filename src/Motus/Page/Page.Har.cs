using System.Text.Json;

namespace Motus;

internal sealed partial class Page
{
    private HarRecorder? _harRecorder;

    /// <summary>
    /// Attaches a HAR recorder to the page's network layer and begins capturing.
    /// Idempotent: a second call while already recording is a no-op.
    /// </summary>
    public Task StartHarRecordingAsync(CancellationToken ct = default)
    {
        if (_networkManager is null)
            throw new InvalidOperationException("Network is not initialized for this page.");

        if (_harRecorder is { IsRecording: true })
            return Task.CompletedTask;

        var recorder = new HarRecorder();
        recorder.EnableRecording();
        _harRecorder = recorder;
        _networkManager.HarRecorder = recorder;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops capturing, writes the accumulated traffic to <paramref name="path"/> as a
    /// standalone HAR 1.2 archive, and detaches the recorder.
    /// </summary>
    public async Task StopHarRecordingAsync(string path, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        var recorder = _harRecorder
            ?? throw new InvalidOperationException("HAR recording was not started. Call StartHarRecordingAsync first.");

        recorder.DisableRecording();
        var archive = new HarArchive(recorder.BuildHarLog());

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        {
            await JsonSerializer.SerializeAsync(
                stream, archive, HarJsonContext.Default.HarArchive, ct).ConfigureAwait(false);
        }

        _harRecorder = null;
        if (_networkManager is not null)
            _networkManager.HarRecorder = null;
    }
}
