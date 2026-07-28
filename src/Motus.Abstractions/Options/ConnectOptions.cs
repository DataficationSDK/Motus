namespace Motus.Abstractions;

/// <summary>
/// Options for connecting to a browser that is already running.
/// </summary>
public sealed record ConnectOptions
{
    /// <summary>
    /// Whether the contexts and pages already open in the browser are adopted on connect,
    /// so they appear in <see cref="IBrowser.Contexts"/> and can be driven. Default: true.
    /// </summary>
    public bool AdoptExistingTargets { get; init; } = true;

    /// <summary>Slows down operations by the specified number of milliseconds.</summary>
    public int SlowMo { get; init; }

    /// <summary>Maximum time in milliseconds to wait for the connection to be established.</summary>
    public int Timeout { get; init; } = 30_000;
}
