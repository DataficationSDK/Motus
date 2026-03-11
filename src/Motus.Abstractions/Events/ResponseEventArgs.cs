namespace Motus.Abstractions;

/// <summary>
/// Event arguments for network response events.
/// </summary>
/// <param name="Response">The response that was received.</param>
public sealed record ResponseEventArgs(IResponse Response);
