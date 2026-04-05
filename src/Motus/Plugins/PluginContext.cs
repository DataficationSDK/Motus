using Motus.Abstractions;

namespace Motus;

internal sealed class PluginContext : IPluginContext
{
    private readonly BrowserContext _context;

    internal PluginContext(BrowserContext context) => _context = context;

    internal BrowserContext Context => _context;

    public void RegisterLifecycleHook(ILifecycleHook hook) =>
        _context.LifecycleHooks.Add(hook);

    public void RegisterWaitCondition(IWaitCondition condition) =>
        _context.RegisterWaitCondition(condition.ConditionName, condition);

    public void RegisterSelectorStrategy(ISelectorStrategy strategy) =>
        _context.SelectorStrategies.Register(strategy);

    public void RegisterReporter(IReporter reporter) =>
        _context.Reporters.Add(reporter);

    public void RegisterAccessibilityRule(IAccessibilityRule rule) =>
        _context.AccessibilityRules.Add(rule);

    public IMotusLogger CreateLogger(string categoryName) => NullMotusLogger.Instance;
}
