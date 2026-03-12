namespace Motus.Abstractions;

/// <summary>
/// Defines a custom wait condition that can be used with the WaitForAsync API.
/// </summary>
public interface IWaitCondition
{
    /// <summary>
    /// Gets the name used in wait expressions (e.g. "animation-complete").
    /// </summary>
    string ConditionName { get; }

    /// <summary>
    /// Returns true when the condition is satisfied. Called repeatedly with configurable
    /// polling interval until it returns true or the timeout elapses.
    /// </summary>
    /// <param name="page">The page to evaluate the condition on.</param>
    /// <param name="options">Polling and timeout configuration. Null uses engine defaults.</param>
    /// <returns>True if the condition is satisfied.</returns>
    Task<bool> EvaluateAsync(IPage page, WaitConditionOptions? options = null);
}
