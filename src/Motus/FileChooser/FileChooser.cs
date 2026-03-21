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
    {
        get
        {
            var page = (Page)Page;
            var handle = SelectorStrategyHelpers.ResolveNodeToHandleAsync(page, BackendNodeId, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Build a unique selector by asking the browser for the element's CSS path
            var selectorJson = page.Session.SendAsync(
                "Runtime.callFunctionOn",
                new RuntimeCallFunctionOnParams(
                    FunctionDeclaration: """
                        function() {
                            const parts = [];
                            let el = this;
                            while (el && el.nodeType === Node.ELEMENT_NODE) {
                                let selector = el.localName;
                                if (el.id) { parts.unshift('#' + el.id); break; }
                                let sib = el, nth = 1;
                                while ((sib = sib.previousElementSibling)) nth++;
                                if (nth > 1) selector += ':nth-child(' + nth + ')';
                                parts.unshift(selector);
                                el = el.parentElement;
                            }
                            return parts.join(' > ');
                        }
                        """,
                    ObjectId: handle.ObjectId,
                    ReturnByValue: true),
                CdpJsonContext.Default.RuntimeCallFunctionOnParams,
                CdpJsonContext.Default.RuntimeCallFunctionOnResult,
                CancellationToken.None).GetAwaiter().GetResult();

            var cssSelector = selectorJson.Result.Value?.ToString() ?? "input[type='file']";
            return new Locator(page, cssSelector);
        }
    }

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
