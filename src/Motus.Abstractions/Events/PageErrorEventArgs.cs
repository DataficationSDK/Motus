namespace Motus.Abstractions;

/// <summary>
/// Event arguments for uncaught exceptions in the page.
/// </summary>
/// <param name="Message">The error message.</param>
/// <param name="Stack">The error stack trace, if available.</param>
public sealed record PageErrorEventArgs(string Message, string? Stack = null);
