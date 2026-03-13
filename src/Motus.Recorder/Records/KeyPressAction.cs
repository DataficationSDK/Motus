namespace Motus.Recorder.Records;

/// <summary>
/// A non-printable key press (not part of an active fill).
/// </summary>
public sealed record KeyPressAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    string Key,
    string Code,
    int Modifiers
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
