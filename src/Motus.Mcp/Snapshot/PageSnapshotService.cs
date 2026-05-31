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
    private IReadOnlyDictionary<long, string>? _backendNodeIdToRef;

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
    public Task<string> TakeSnapshotAsync(CancellationToken ct = default)
        => TakeSnapshotAsync(rootRef: null, maxDepth: null, ct);

    /// <summary>
    /// Fetches a fresh snapshot and renders it, optionally scoped. When
    /// <paramref name="rootRef"/> is given, the snapshot is rooted at the subtree
    /// of that ref (resolved against the previous snapshot's map). When
    /// <paramref name="maxDepth"/> is given, the tree is rendered only that many
    /// levels deep. Replaces the ref map with the refs assigned this call.
    /// </summary>
    /// <exception cref="SnapshotNotTakenException">
    /// <paramref name="rootRef"/> was given but no earlier snapshot exists to resolve it against.
    /// </exception>
    /// <exception cref="StaleRefException">
    /// <paramref name="rootRef"/> is not in the previous snapshot, or its element is no longer present.
    /// </exception>
    public async Task<string> TakeSnapshotAsync(string? rootRef, int? maxDepth, CancellationToken ct = default)
    {
        long? rootBackendNodeId = null;
        if (rootRef is not null)
        {
            if (_refToBackendNodeId is null)
                throw new SnapshotNotTakenException();

            if (!_refToBackendNodeId.TryGetValue(rootRef, out var backendNodeId))
                throw new StaleRefException(rootRef);

            rootBackendNodeId = backendNodeId;
        }

        var snapshot = await _page.AccessibilitySnapshotAsync(ct).ConfigureAwait(false);

        SerializedSnapshot serialized;
        if (rootBackendNodeId is { } id)
        {
            var rootNode = SnapshotSerializer.FindByBackendId(snapshot.Roots, id)
                ?? throw new StaleRefException(rootRef!);
            serialized = SnapshotSerializer.Serialize([rootNode], maxDepth);
        }
        else
        {
            serialized = SnapshotSerializer.Serialize(snapshot.Roots, maxDepth);
        }

        _refToBackendNodeId = serialized.RefToBackendNodeId;
        _backendNodeIdToRef = BuildReverseMap(serialized.RefToBackendNodeId);
        LastSnapshot = serialized.Text;
        return serialized.Text;
    }

    private static Dictionary<long, string> BuildReverseMap(IReadOnlyDictionary<string, long> forward)
    {
        // The forward map is 1:1 (each ref maps to a distinct backend node), so a
        // straight inversion is unambiguous.
        var reverse = new Dictionary<long, string>(forward.Count);
        foreach (var (refId, backendNodeId) in forward)
            reverse[backendNodeId] = refId;
        return reverse;
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

    /// <summary>
    /// Returns the ref the current snapshot assigned to the given backend DOM node,
    /// or null when no snapshot has been taken or the node was not assigned a ref
    /// (for example, a node the snapshot does not address).
    /// </summary>
    public string? GetRefForNodeId(long backendNodeId)
        => _backendNodeIdToRef is not null
            && _backendNodeIdToRef.TryGetValue(backendNodeId, out var refId)
            ? refId
            : null;
}
