namespace Motus.Abstractions;

/// <summary>
/// Event arguments for console messages emitted by the page.
/// </summary>
/// <param name="Type">The type of the console message (e.g. "log", "error", "warning").</param>
/// <param name="Text">The text of the console message.</param>
public sealed record ConsoleMessageEventArgs(string Type, string Text);
