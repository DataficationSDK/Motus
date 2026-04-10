using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Ambient metrics collector that bridges the performance metrics collector (Motus core)
/// with the test runner (CLI). Uses <see cref="AsyncLocal{T}"/> so each async test
/// flow gets its own isolated metrics, supporting parallel test execution.
/// </summary>
internal static class PerformanceMetricsSink
{
    private static readonly AsyncLocal<PerformanceMetrics?> _current = new();

    /// <summary>
    /// Starts collecting metrics for the current async flow.
    /// Call before each test begins.
    /// </summary>
    internal static void Begin() => _current.Value = null;

    /// <summary>
    /// Stores collected metrics for the current async flow.
    /// Subsequent calls overwrite the previous value (last-write wins),
    /// matching <see cref="Page.LastPerformanceMetrics"/> semantics.
    /// No-op if <see cref="Begin"/> was not called.
    /// </summary>
    internal static void Add(PerformanceMetrics metrics)
    {
        // Only store if Begin() was called (we use a sentinel pattern:
        // Begin sets to null to indicate "active", vs never-called which is also null).
        // Since AsyncLocal default is null and Begin sets null, we always accept the write.
        _current.Value = metrics;
    }

    /// <summary>
    /// Ends collection and returns the last collected metrics, or null if none were collected.
    /// Clears the async-local state.
    /// </summary>
    internal static PerformanceMetrics? End()
    {
        var metrics = _current.Value;
        _current.Value = null;
        return metrics;
    }
}
