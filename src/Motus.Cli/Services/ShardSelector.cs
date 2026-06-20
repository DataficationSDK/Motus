namespace Motus.Cli.Services;

/// <summary>
/// Partitions a discovered test list into one deterministic shard. Sharding lets a large suite
/// be split across independent <c>motus run</c> processes (typically one per CI agent): each
/// process selects a disjoint subset, and because the partition is a pure function of the
/// discovered set and the shard coordinates, no coordination between agents is needed.
/// </summary>
public static class ShardSelector
{
    /// <summary>
    /// Returns the subset of <paramref name="tests"/> belonging to shard <paramref name="index"/>
    /// of <paramref name="total"/>. The full set is first sorted by a stable key
    /// (assembly path + fully qualified name, ordinal) so the order is identical across machines
    /// regardless of reflection enumeration order, then assigned round-robin by
    /// <c>position % total == index - 1</c>. Round-robin over a stable sort spreads slow classes
    /// across shards without needing timing data.
    /// </summary>
    /// <param name="tests">The discovered tests to partition.</param>
    /// <param name="index">The 1-based shard index (1..total).</param>
    /// <param name="total">The total number of shards (>= 1).</param>
    public static List<DiscoveredTest> Select(IReadOnlyList<DiscoveredTest> tests, int index, int total)
    {
        if (total < 1)
            throw new ArgumentOutOfRangeException(nameof(total), total, "Shard total must be at least 1.");
        if (index < 1 || index > total)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Shard index must be between 1 and {total}.");

        var zeroBased = index - 1;
        var ordered = tests
            .OrderBy(SortKey, StringComparer.Ordinal)
            .ToList();

        var shard = new List<DiscoveredTest>();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i % total == zeroBased)
                shard.Add(ordered[i]);
        }

        return shard;
    }

    /// <summary>
    /// Parses a <c>"index/total"</c> shard spec (1-based index), validating
    /// <c>total >= 1</c> and <c>1 &lt;= index &lt;= total</c>.
    /// </summary>
    public static bool TryParse(string spec, out int index, out int total, out string? error)
    {
        index = 0;
        total = 0;
        error = null;

        var parts = spec.Split('/');
        if (parts.Length != 2
            || !int.TryParse(parts[0].Trim(), out index)
            || !int.TryParse(parts[1].Trim(), out total))
        {
            error = $"Invalid --shard '{spec}'. Expected the form <index>/<total>, e.g. 1/4.";
            return false;
        }

        if (total < 1)
        {
            error = $"Invalid --shard '{spec}'. The total must be at least 1.";
            return false;
        }

        if (index < 1 || index > total)
        {
            error = $"Invalid --shard '{spec}'. The index must be between 1 and {total}.";
            return false;
        }

        return true;
    }

    private static string SortKey(DiscoveredTest test) =>
        $"{test.TestClass.Assembly.Location}::{test.FullName}";
}
