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
        => throw new NotImplementedException("FileChooser.Element requires DOM.resolveNode support.");

    public bool IsMultiple { get; }

    internal int BackendNodeId { get; }

    public async Task SetFilesAsync(IEnumerable<FilePayload> files, CancellationToken ct = default)
    {
        var tempFiles = new List<string>();
        try
        {
            foreach (var file in files)
            {
                var tempPath = Path.Combine(Path.GetTempPath(), file.Name);
                await File.WriteAllBytesAsync(tempPath, file.Buffer, ct).ConfigureAwait(false);
                tempFiles.Add(tempPath);
            }

            var page = (Page)Page;
            await page.Session.SendAsync(
                "DOM.setFileInputFiles",
                new DomSetFileInputFilesParams(Files: tempFiles.ToArray()),
                CdpJsonContext.Default.DomSetFileInputFilesParams,
                CdpJsonContext.Default.DomSetFileInputFilesResult,
                ct).ConfigureAwait(false);
        }
        finally
        {
            foreach (var tempFile in tempFiles)
            {
                try { File.Delete(tempFile); } catch { /* best effort */ }
            }
        }
    }
}
