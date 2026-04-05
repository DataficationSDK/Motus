using Motus.Abstractions;

namespace Motus;

internal sealed class AccessibilityRuleCollection
{
    private readonly List<IAccessibilityRule> _rules = [];

    internal void Add(IAccessibilityRule rule)
    {
        lock (_rules)
            _rules.Add(rule);
    }

    internal IAccessibilityRule[] Snapshot()
    {
        lock (_rules)
            return [.. _rules];
    }
}
