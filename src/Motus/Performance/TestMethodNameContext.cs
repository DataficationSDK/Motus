namespace Motus;

/// <summary>
/// Carries the current test method name across async boundaries.
/// The visual runner sets this before invoking test lifecycle methods so that
/// <c>MotusTestBase</c> can resolve per-method attributes (e.g. <c>[PerformanceBudget]</c>)
/// without requiring a framework-specific <c>TestContext</c>.
/// </summary>
public static class TestMethodNameContext
{
    private static readonly AsyncLocal<string?> s_name = new();

    /// <summary>Sets the active test method name for the current async flow.</summary>
    public static void Set(string? methodName) => s_name.Value = methodName;

    /// <summary>Clears the active test method name.</summary>
    public static void Clear() => s_name.Value = null;

    /// <summary>The active test method name, or null if not set.</summary>
    public static string? Current => s_name.Value;
}
