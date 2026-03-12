using Motus.Abstractions;

namespace Motus;

internal sealed class PluginContext : IPluginContext
{
    private readonly BrowserContext _context;

    internal PluginContext(BrowserContext context) => _context = context;

    public void RegisterLifecycleHook(ILifecycleHook hook) =>
        _context.LifecycleHooks.Add(hook);

    public void RegisterWaitCondition(IWaitCondition condition) =>
        _context.RegisterWaitCondition(condition.ConditionName, condition);

    public void RegisterSelectorStrategy(ISelectorStrategy strategy) =>
        _context.SelectorStrategies.Register(strategy);

    public void RegisterReporter(IReporter reporter) =>
        throw new NotImplementedException("Phase 5A.");

    public IMotusLogger CreateLogger(string categoryName) => NullMotusLogger.Instance;
}
