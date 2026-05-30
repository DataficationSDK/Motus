namespace Motus.Mcp;

/// <summary>
/// Thrown when a ref is resolved before any snapshot has been taken on the page.
/// </summary>
public sealed class SnapshotNotTakenException : Exception
{
    public SnapshotNotTakenException()
        : base("No accessibility snapshot has been taken yet; take a snapshot before resolving a ref.")
    {
    }
}

/// <summary>
/// Thrown when a ref is not present in the current snapshot. A ref is valid only
/// for the snapshot that produced it; taking a new snapshot invalidates earlier refs.
/// </summary>
public sealed class StaleRefException : Exception
{
    public StaleRefException(string refId)
        : base($"Ref '{refId}' is not in the current snapshot; take a new snapshot and retry.")
    {
        RefId = refId;
    }

    /// <summary>The ref that could not be resolved.</summary>
    public string RefId { get; }
}
