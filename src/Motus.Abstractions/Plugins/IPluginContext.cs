namespace Motus.Abstractions;

/// <summary>
/// Provides services to plugins during initialization for registering custom extensions.
/// </summary>
public interface IPluginContext
{
    /// <summary>
    /// Registers a custom selector strategy.
    /// </summary>
    /// <param name="strategy">The selector strategy to register.</param>
    void RegisterSelectorStrategy(ISelectorStrategy strategy);

    /// <summary>
    /// Registers a custom wait condition.
    /// </summary>
    /// <param name="condition">The wait condition to register.</param>
    void RegisterWaitCondition(IWaitCondition condition);

    /// <summary>
    /// Registers a lifecycle hook.
    /// </summary>
    /// <param name="hook">The lifecycle hook to register.</param>
    void RegisterLifecycleHook(ILifecycleHook hook);

    /// <summary>
    /// Registers a test reporter.
    /// </summary>
    /// <param name="reporter">The reporter to register.</param>
    void RegisterReporter(IReporter reporter);

    /// <summary>
    /// Registers a custom accessibility rule that will be invoked during accessibility audits.
    /// </summary>
    /// <param name="rule">The accessibility rule to register.</param>
    void RegisterAccessibilityRule(IAccessibilityRule rule);

    /// <summary>
    /// Creates a logger scoped to the calling plugin.
    /// </summary>
    /// <param name="categoryName">The log category name.</param>
    /// <returns>A logger instance.</returns>
    IMotusLogger CreateLogger(string categoryName);
}
