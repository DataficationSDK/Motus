namespace Motus.Abstractions;

/// <summary>
/// Represents the outcome of a browser action performed by the automation engine.
/// </summary>
/// <param name="Action">The action that was performed (e.g. "click", "fill").</param>
/// <param name="Error">The exception that caused the action to fail, or null if the action succeeded.</param>
public sealed record ActionResult(string Action, Exception? Error = null);
