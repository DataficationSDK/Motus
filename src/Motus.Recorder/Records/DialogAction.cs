namespace Motus.Recorder.Records;

/// <summary>
/// A JavaScript dialog (alert, confirm, prompt, beforeunload).
/// </summary>
public sealed record DialogAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    string DialogType,
    bool Accepted,
    string? PromptText
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
