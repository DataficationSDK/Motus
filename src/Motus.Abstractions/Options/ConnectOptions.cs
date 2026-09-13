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
    /// <remarks>
    /// This decides what is taken over, not what is watched. With it off, a tab opened by whoever
    /// else is using the browser stays out of Motus, but a popup opened by a page of a context
    /// Motus created is still tracked and still raises <see cref="IPage.Popup"/>.
    /// </remarks>
    public bool AdoptExistingTargets { get; init; } = true;

    /// <summary>Slows down operations by the specified number of milliseconds.</summary>
    public int SlowMo { get; init; }

    /// <summary>Maximum time in milliseconds to wait for the connection to be established.</summary>
    public int Timeout { get; init; } = 30_000;
}
