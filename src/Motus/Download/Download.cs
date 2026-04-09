using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents a file download initiated by a page.
/// </summary>
internal sealed class Download : IDownload
{
    private readonly TaskCompletionSource<DownloadOutcome> _completionTcs = new();
    private string? _tempPath;

    internal Download(string guid, string url, string suggestedFilename)
    {
        Guid = guid;
        Url = url;
        SuggestedFilename = suggestedFilename;
    }

    internal string Guid { get; }

    public string Url { get; }

    public string SuggestedFilename { get; }

    internal void OnProgress(string state, string? error = null)
    {
        switch (state)
        {
            case "completed":
                _completionTcs.TrySetResult(new DownloadOutcome(true, null));
                break;
            case "canceled":
                _completionTcs.TrySetResult(new DownloadOutcome(false, "Download was canceled"));
                break;
        }
    }

    internal void SetTempPath(string path) => _tempPath = path;

    public async Task<string?> PathAsync()
    {
        var outcome = await _completionTcs.Task.ConfigureAwait(false);
        return outcome.Success ? _tempPath : null;
    }

    public async Task<string?> FailureAsync()
    {
        var outcome = await _completionTcs.Task.ConfigureAwait(false);
        return outcome.Error;
    }

    public async Task SaveAsAsync(string path)
    {
        var outcome = await _completionTcs.Task.ConfigureAwait(false);
        if (!outcome.Success)
            throw new InvalidOperationException($"Download failed: {outcome.Error}");

        if (_tempPath is not null)
            File.Copy(_tempPath, path, overwrite: true);
    }

    public Task DeleteAsync()
    {
        if (_tempPath is not null && File.Exists(_tempPath))
            File.Delete(_tempPath);
        return Task.CompletedTask;
    }

    public Task CancelAsync()
        => throw new NotSupportedException("Download cancellation requires Fetch domain support.");

    private readonly record struct DownloadOutcome(bool Success, string? Error);
}
