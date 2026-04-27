using Motus.Abstractions;

namespace Motus.Runner.Services.Coverage;

/// <summary>
/// Holds the latest aggregated coverage snapshot for the visual runner. Receives
/// data via <see cref="Set"/> from the test execution pipeline (or via direct
/// calls when a CLI command pre-populates the runner with an existing report).
/// </summary>
public interface ICoverageService
{
    /// <summary>The most recent aggregated coverage snapshot, or null if none has been recorded.</summary>
    CoverageData? Latest { get; }

    /// <summary>True when <see cref="Latest"/> is non-null and contains at least one script or stylesheet.</summary>
    bool HasData { get; }

    /// <summary>Raised when <see cref="Latest"/> changes.</summary>
    event Action? CoverageChanged;

    /// <summary>Stores a new aggregated coverage snapshot and raises <see cref="CoverageChanged"/>.</summary>
    void Set(CoverageData coverage);

    /// <summary>Clears the stored snapshot and raises <see cref="CoverageChanged"/>.</summary>
    void Clear();
}
