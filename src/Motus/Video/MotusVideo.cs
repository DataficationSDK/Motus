using Motus.Abstractions;

namespace Motus;

/// <summary>
/// IVideo implementation backed by an AVI file on disk.
/// </summary>
internal sealed class MotusVideo : IVideo
{
    private readonly string _path;
    private readonly Task _completionTask;

    internal MotusVideo(string path, Task completionTask)
    {
        _path = path;
        _completionTask = completionTask;
    }

    public async Task<string> PathAsync()
    {
        await _completionTask.ConfigureAwait(false);
        return _path;
    }

    public async Task SaveAsAsync(string path)
    {
        await _completionTask.ConfigureAwait(false);
        File.Copy(_path, path, overwrite: true);
    }

    public async Task DeleteAsync()
    {
        await _completionTask.ConfigureAwait(false);
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
