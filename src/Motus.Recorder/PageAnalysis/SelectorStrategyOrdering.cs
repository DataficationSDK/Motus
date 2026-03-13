using Motus.Abstractions;

namespace Motus.Recorder.PageAnalysis;

/// <summary>
/// Reorders selector strategies based on a user-specified priority list.
/// </summary>
internal static class SelectorStrategyOrdering
{
    /// <summary>
    /// Returns strategies reordered so that those matching <paramref name="priorityNames"/>
    /// appear first (in the specified order), followed by any remaining strategies in their
    /// original priority order. Names are matched case-insensitively.
    /// </summary>
    internal static IReadOnlyList<ISelectorStrategy> Reorder(
        IReadOnlyList<ISelectorStrategy> strategies,
        IReadOnlyList<string>? priorityNames)
    {
        if (priorityNames is null or { Count: 0 })
            return strategies;

        var byName = new Dictionary<string, ISelectorStrategy>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in strategies)
            byName[s.StrategyName] = s;

        var result = new List<ISelectorStrategy>(strategies.Count);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add strategies in priority order
        foreach (var name in priorityNames)
        {
            if (byName.TryGetValue(name, out var strategy) && used.Add(name))
                result.Add(strategy);
        }

        // Add remaining strategies in their original priority order
        foreach (var strategy in strategies)
        {
            if (used.Add(strategy.StrategyName))
                result.Add(strategy);
        }

        return result;
    }
}
