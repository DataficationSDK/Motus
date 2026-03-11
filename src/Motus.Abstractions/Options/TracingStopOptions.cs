namespace Motus.Abstractions;

/// <summary>
/// Options for stopping a trace.
/// </summary>
public sealed record TracingStopOptions
{
    /// <summary>Path to export the trace to.</summary>
    public string? Path { get; init; }
}
