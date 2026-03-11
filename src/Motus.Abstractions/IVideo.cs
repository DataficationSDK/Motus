namespace Motus.Abstractions;

/// <summary>
/// Represents a video recording of page actions.
/// </summary>
public interface IVideo
{
    /// <summary>
    /// Returns the file system path where the video is saved.
    /// </summary>
    /// <returns>The path to the video file.</returns>
    Task<string> PathAsync();

    /// <summary>
    /// Saves the video to the specified path.
    /// </summary>
    /// <param name="path">The destination file path.</param>
    Task SaveAsAsync(string path);

    /// <summary>
    /// Deletes the video file.
    /// </summary>
    Task DeleteAsync();
}
