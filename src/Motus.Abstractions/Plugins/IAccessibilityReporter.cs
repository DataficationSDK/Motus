namespace Motus.Abstractions;

/// <summary>
/// Opt-in interface for reporters that want to receive accessibility violation events.
/// Reporters implement both <see cref="IReporter"/> and <see cref="IAccessibilityReporter"/>
/// to receive violations alongside normal test lifecycle events.
/// </summary>
/// <remarks>
/// This is a separate interface (rather than a default method on <see cref="IReporter"/>)
/// to avoid NativeAOT trimming issues with default interface method implementations.
/// The reporter dispatch checks <c>reporter is IAccessibilityReporter</c> at runtime.
/// </remarks>
public interface IAccessibilityReporter
{
    /// <summary>
    /// Called when an accessibility violation is detected during test execution.
    /// </summary>
    /// <param name="violation">The accessibility violation details.</param>
    /// <param name="test">The test that was running when the violation was detected.</param>
    Task OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test);
}
