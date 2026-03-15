namespace Motus.Recorder.Records;

/// <summary>
/// Base record for all captured browser actions.
/// </summary>
public abstract record ActionRecord(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y
)
{
    /// <summary>
    /// JS-side target element ID captured at event time, used for deferred selector inference.
    /// </summary>
    public int? TargetId { get; init; }
}
