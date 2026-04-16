namespace Motus.Recorder.Records;

/// <summary>
/// Wraps an <see cref="ActionRecord"/> with the inferred selector for its target element.
/// <see cref="Selector"/> is null when inference fails or the action type needs no selector
/// (navigation, keyboard, scroll, dialog).
/// </summary>
/// <param name="Source">The raw captured action.</param>
/// <param name="Selector">Inferred locator string, or null when inference is not applicable.</param>
/// <param name="LocatorMethod">The locator factory method used for the selector (e.g. "Locator").</param>
/// <param name="BackendNodeId">The CDP backend node ID of the target element, when resolvable.</param>
public sealed record ResolvedAction(
    ActionRecord Source,
    string? Selector,
    string? LocatorMethod = null,
    int? BackendNodeId = null);
