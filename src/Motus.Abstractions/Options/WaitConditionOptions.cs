namespace Motus.Abstractions;

/// <summary>
/// Options that control how a wait condition is evaluated.
/// </summary>
public sealed record WaitConditionOptions
{
    /// <summary>Maximum time in milliseconds to wait before the condition is considered failed. Null uses the engine default.</summary>
    public int? Timeout { get; init; }

    /// <summary>Interval in milliseconds between condition evaluations. Null uses the engine default.</summary>
    public int? PollingInterval { get; init; }
}
