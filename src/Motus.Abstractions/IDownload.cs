namespace Motus.Abstractions;

/// <summary>
/// Represents a file download initiated by a page.
/// </summary>
public interface IDownload
{
    /// <summary>
    /// Gets the URL of the download.
    /// </summary>
    string Url { get; }

    /// <summary>
    /// Gets the suggested filename for the download.
    /// </summary>
    string SuggestedFilename { get; }

    /// <summary>
    /// Saves the download to the specified path.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    Task SaveAsAsync(string path);

    /// <summary>
    /// Returns the path to the downloaded file once the download completes.
    /// </summary>
    /// <returns>The path to the downloaded file, or null if the download failed.</returns>
    Task<string?> PathAsync();

    /// <summary>
    /// Deletes the downloaded file.
    /// </summary>
    Task DeleteAsync();

    /// <summary>
    /// Cancels the download.
    /// </summary>
    Task CancelAsync();
}
