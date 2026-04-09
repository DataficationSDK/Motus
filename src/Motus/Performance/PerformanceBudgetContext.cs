using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Ambient performance budget for the current async test flow.
/// Test framework adapters push the resolved budget (from <see cref="PerformanceBudgetAttribute"/>)
/// at test setup and clear it at teardown. The assertion reads it via <see cref="Current"/>.
/// Uses <see cref="AsyncLocal{T}"/> so parallel tests remain isolated.
/// </summary>
public static class PerformanceBudgetContext
{
    private static readonly AsyncLocal<PerformanceBudget?> s_current = new();

    /// <summary>Sets the active budget for the current async flow.</summary>
    public static void Push(PerformanceBudget? budget) => s_current.Value = budget;

    /// <summary>Clears the active budget.</summary>
    public static void Clear() => s_current.Value = null;

    /// <summary>The active budget for the current async flow, or null if none set.</summary>
    internal static PerformanceBudget? Current => s_current.Value;
}
