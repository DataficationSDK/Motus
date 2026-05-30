using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Holds the most recent accessibility snapshot for a single page and resolves
/// the refs it assigned back to actionable locators. Refs are valid only for the
/// latest snapshot; taking a new snapshot replaces the ref map.
/// </summary>
public sealed class PageSnapshotService
{
    private readonly IPage _page;
    private IReadOnlyDictionary<string, long>? _refToBackendNodeId;

    public PageSnapshotService(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
    }

    /// <summary>The text of the most recent snapshot, or null if none has been taken.</summary>
    public string? LastSnapshot { get; private set; }

    /// <summary>
    /// Fetches a fresh accessibility snapshot, assigns refs in document order, and
    /// returns the indented text representation. Replaces any earlier ref map.
    /// </summary>
    public async Task<string> TakeSnapshotAsync(CancellationToken ct = default)
    {
        var snapshot = await _page.AccessibilitySnapshotAsync(ct).ConfigureAwait(false);
        var serialized = SnapshotSerializer.Serialize(snapshot);

        _refToBackendNodeId = serialized.RefToBackendNodeId;
        LastSnapshot = serialized.Text;
        return serialized.Text;
    }

    /// <summary>
    /// Resolves a ref from the current snapshot to a locator. The element is
    /// resolved lazily when an action runs on the returned locator; if it has since
    /// detached from the document, that action fails.
    /// </summary>
    /// <exception cref="SnapshotNotTakenException">No snapshot has been taken yet.</exception>
    /// <exception cref="StaleRefException">The ref is not in the current snapshot.</exception>
    public ILocator ResolveRef(string refId)
    {
        if (_refToBackendNodeId is null)
            throw new SnapshotNotTakenException();

        if (!_refToBackendNodeId.TryGetValue(refId, out var backendNodeId))
            throw new StaleRefException(refId);

        return _page.LocatorByBackendNodeId(backendNodeId);
    }
}
