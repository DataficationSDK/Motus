namespace Motus.Abstractions;

/// <summary>
/// Options for mouse move operations.
/// </summary>
/// <param name="Steps">Number of intermediate steps for the mouse movement.</param>
public sealed record MouseMoveOptions(int? Steps = null);
