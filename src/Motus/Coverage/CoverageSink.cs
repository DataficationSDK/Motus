using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Ambient coverage data accumulator that bridges <see cref="CoverageCollector"/>
/// with downstream consumers (CLI test runner, reporters). Uses <see cref="AsyncLocal{T}"/>
/// so each async test flow gets its own isolated coverage list, supporting parallel
/// test execution.
/// </summary>
/// <remarks>
/// The list reference is established by <see cref="Begin"/> in the parent (caller)
/// flow. <see cref="AsyncLocal{T}"/> propagates that reference down to child flows
/// (the lifecycle hook), so when the hook calls <see cref="Add"/> it mutates the
/// same list the parent will read in <see cref="End"/>. Assigning the AsyncLocal
/// from a child flow would not propagate back up.
/// </remarks>
internal static class CoverageSink
{
    private static readonly AsyncLocal<List<CoverageData>?> _current = new();

    /// <summary>Starts collecting coverage for the current async flow. Call before each test begins.</summary>
    internal static void Begin() => _current.Value = new List<CoverageData>();

    /// <summary>Appends coverage data for the current async flow. No-op if <see cref="Begin"/> was not called.</summary>
    internal static void Add(CoverageData data) => _current.Value?.Add(data);

    /// <summary>
    /// Ends collection and returns the most recent coverage snapshot (or null if none),
    /// preserving the prior single-snapshot semantics for callers.
    /// </summary>
    internal static CoverageData? End()
    {
        var list = _current.Value;
        _current.Value = null;
        if (list is null || list.Count == 0)
            return null;
        return list[^1];
    }

    /// <summary>
    /// Ends collection and returns all coverage snapshots gathered since <see cref="Begin"/>.
    /// Returns an empty list if <see cref="Begin"/> was not called.
    /// </summary>
    internal static IReadOnlyList<CoverageData> EndAll()
    {
        var list = _current.Value;
        _current.Value = null;
        return list is { Count: > 0 } ? list : Array.Empty<CoverageData>();
    }
}
