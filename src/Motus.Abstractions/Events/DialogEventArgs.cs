namespace Motus.Abstractions;

/// <summary>
/// Event arguments for JavaScript dialog events.
/// </summary>
/// <param name="Dialog">The dialog that was opened.</param>
public sealed record DialogEventArgs(IDialog Dialog);
