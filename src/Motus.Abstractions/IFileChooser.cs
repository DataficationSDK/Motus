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
    /// Gets the input element associated with the file chooser.
    /// </summary>
    IElementHandle Element { get; }

    /// <summary>
    /// Gets whether the file chooser allows multiple file selection.
    /// </summary>
    bool IsMultiple { get; }

    /// <summary>
    /// Sets the files for the file chooser.
    /// </summary>
    /// <param name="files">The files to select.</param>
    Task SetFilesAsync(IEnumerable<FilePayload> files);
}
