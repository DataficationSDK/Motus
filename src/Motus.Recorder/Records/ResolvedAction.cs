namespace Motus.Recorder.Records;

/// <summary>
/// Wraps an <see cref="ActionRecord"/> with the inferred selector for its target element.
/// <see cref="Selector"/> is null when inference fails or the action type needs no selector
/// (navigation, keyboard, scroll, dialog).
/// </summary>
public sealed record ResolvedAction(ActionRecord Source, string? Selector);
