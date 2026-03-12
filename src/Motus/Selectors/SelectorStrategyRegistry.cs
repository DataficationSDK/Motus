using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Thread-safe registry of selector strategies keyed by prefix (case-insensitive).
/// </summary>
internal sealed class SelectorStrategyRegistry
{
    private readonly Dictionary<string, ISelectorStrategy> _strategies = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    internal void Register(ISelectorStrategy strategy)
    {
        lock (_lock)
            _strategies[strategy.StrategyName] = strategy;
    }

    internal bool TryGetStrategy(string prefix, out ISelectorStrategy? strategy)
    {
        lock (_lock)
            return _strategies.TryGetValue(prefix, out strategy);
    }

    internal ISelectorStrategy GetDefault()
    {
        lock (_lock)
            return _strategies["css"];
    }

    internal IReadOnlyList<ISelectorStrategy> GetAllByPriority()
    {
        lock (_lock)
            return _strategies.Values.OrderByDescending(s => s.Priority).ToList();
    }
}
