using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Ambient violation collector that bridges the accessibility audit hook (Motus core)
/// with the test runner (CLI). Uses <see cref="AsyncLocal{T}"/> so each async test
/// flow gets its own isolated violation list, supporting parallel test execution.
/// </summary>
internal static class AccessibilityViolationSink
{
    private static readonly AsyncLocal<List<AccessibilityViolation>?> _current = new();

    /// <summary>
    /// Starts collecting violations for the current async flow.
    /// Call before each test begins.
    /// </summary>
    internal static void Begin() => _current.Value = [];

    /// <summary>
    /// Adds a violation to the current async flow's collection.
    /// No-op if <see cref="Begin"/> was not called.
    /// </summary>
    internal static void Add(AccessibilityViolation violation) => _current.Value?.Add(violation);

    /// <summary>
    /// Ends collection and returns all violations gathered since <see cref="Begin"/>.
    /// Clears the async-local state. Returns an empty list if <see cref="Begin"/> was not called.
    /// </summary>
    internal static IReadOnlyList<AccessibilityViolation> End()
    {
        var list = _current.Value;
        _current.Value = null;
        return list is { Count: > 0 } ? list : [];
    }
}
