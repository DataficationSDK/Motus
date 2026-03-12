using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents a file chooser dialog opened by the page.
/// </summary>
internal sealed class FileChooser : IFileChooser
{
    internal FileChooser(IPage page, bool isMultiple, int backendNodeId)
    {
        Page = page;
        IsMultiple = isMultiple;
        BackendNodeId = backendNodeId;
    }

    public IPage Page { get; }

    public ILocator Element
        => throw new NotImplementedException("FileChooser.Element requires Locator support (Phase 1I).");

    public bool IsMultiple { get; }

    internal int BackendNodeId { get; }

    public Task SetFilesAsync(IEnumerable<FilePayload> files, CancellationToken ct = default)
        => throw new NotImplementedException("FileChooser.SetFilesAsync requires DOM.setFileInputFiles (Phase 1I).");
}
