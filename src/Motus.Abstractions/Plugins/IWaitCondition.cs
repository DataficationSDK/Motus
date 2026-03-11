namespace Motus.Abstractions;

/// <summary>
/// Defines a custom condition that can be awaited during automation.
/// </summary>
public interface IWaitCondition
{
    /// <summary>
    /// Gets the name of this wait condition.
    /// </summary>
    string ConditionName { get; }

    /// <summary>
    /// Evaluates the condition on the given page.
    /// </summary>
    /// <param name="page">The page to evaluate the condition on.</param>
    /// <param name="timeout">Maximum time in milliseconds to wait.</param>
    /// <returns>True if the condition is satisfied.</returns>
    Task<bool> EvaluateAsync(IPage page, double? timeout = null);
}
