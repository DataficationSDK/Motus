using Motus.Abstractions;
using Motus.Recorder.Records;

namespace Motus.Recorder;

/// <summary>
/// Captures browser interactions and produces a stream of <see cref="ResolvedAction"/> objects.
/// </summary>
public interface IActionCaptureEngine : IAsyncDisposable
{
    /// <summary>
    /// Starts recording actions on the given page.
    /// </summary>
    Task StartAsync(IPage page, CancellationToken ct = default);

    /// <summary>
    /// Stops recording, flushes pending actions, and completes the action stream.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Whether the engine is currently recording.
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// Async stream of resolved actions. Completes when recording stops.
    /// </summary>
    IAsyncEnumerable<ResolvedAction> Actions { get; }

    /// <summary>
    /// Snapshot of all resolved actions captured so far.
    /// </summary>
    IReadOnlyList<ResolvedAction> CapturedActions { get; }
}
