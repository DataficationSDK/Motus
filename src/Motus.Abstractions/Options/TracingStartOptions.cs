namespace Motus.Abstractions;

/// <summary>
/// Options for starting a trace.
/// </summary>
public sealed record TracingStartOptions
{
    /// <summary>Whether to capture screenshots during the trace.</summary>
    public bool? Screenshots { get; init; }

    /// <summary>Whether to capture DOM snapshots during the trace.</summary>
    public bool? Snapshots { get; init; }

    /// <summary>Whether to include source files in the trace.</summary>
    public bool? Sources { get; init; }

    /// <summary>Trace name to display in the trace viewer.</summary>
    public string? Name { get; init; }
}
