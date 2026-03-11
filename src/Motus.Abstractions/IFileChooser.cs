namespace Motus.Abstractions;

/// <summary>
/// Represents a file chooser dialog opened by the page.
/// </summary>
public interface IFileChooser
{
    /// <summary>
    /// Gets the page that this file chooser belongs to.
    /// </summary>
    IPage Page { get; }

    /// <summary>
    /// Gets the locator for the input element associated with the file chooser.
    /// </summary>
    ILocator Element { get; }

    /// <summary>
    /// Gets whether the file chooser allows multiple file selection.
    /// </summary>
    bool IsMultiple { get; }

    /// <summary>
    /// Sets the files for the file chooser.
    /// </summary>
    /// <param name="files">The files to select.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SetFilesAsync(IEnumerable<FilePayload> files, CancellationToken ct = default);
}
