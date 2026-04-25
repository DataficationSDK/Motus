using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Ambient coverage data accumulator that bridges <see cref="CoverageCollector"/>
/// with downstream consumers (CLI test runner, reporters). Uses <see cref="AsyncLocal{T}"/>
/// so each async test flow gets its own isolated coverage data, supporting parallel
/// test execution.
/// </summary>
internal static class CoverageSink
{
    private static readonly AsyncLocal<CoverageData?> _current = new();

    /// <summary>Starts collecting coverage for the current async flow. Call before each test begins.</summary>
    internal static void Begin() => _current.Value = null;

    /// <summary>Stores coverage data for the current async flow. Last write wins.</summary>
    internal static void Add(CoverageData data) => _current.Value = data;

    /// <summary>Ends collection and returns the last collected coverage, or null if none.</summary>
    internal static CoverageData? End()
    {
        var data = _current.Value;
        _current.Value = null;
        return data;
    }
}
