namespace Motus.Abstractions;

/// <summary>
/// Options for mouse move operations.
/// </summary>
/// <param name="Steps">Number of intermediate steps for the mouse movement.</param>
/// <param name="Modifiers">Modifier keys reported as held during the move.</param>
public sealed record MouseMoveOptions(
    int? Steps = null,
    KeyModifier Modifiers = KeyModifier.None);
