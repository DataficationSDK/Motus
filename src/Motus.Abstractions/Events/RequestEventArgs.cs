namespace Motus.Abstractions;

/// <summary>
/// Event arguments for network request events.
/// </summary>
/// <param name="Request">The request that was made.</param>
public sealed record RequestEventArgs(IRequest Request);
