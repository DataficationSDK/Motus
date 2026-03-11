namespace Motus.Abstractions;

/// <summary>
/// Provides tracing capabilities for recording browser actions.
/// </summary>
public interface ITracing
{
    /// <summary>
    /// Starts a new trace.
    /// </summary>
    /// <param name="options">Options for the trace.</param>
    Task StartAsync(TracingStartOptions? options = null);

    /// <summary>
    /// Stops the trace and optionally exports it to a file.
    /// </summary>
    /// <param name="options">Options for stopping the trace.</param>
    Task StopAsync(TracingStopOptions? options = null);
}
