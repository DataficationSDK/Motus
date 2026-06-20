namespace Motus.Abstractions;

/// <summary>
/// Metadata for a test suite run, passed to the reporter when a run begins.
/// </summary>
/// <param name="SuiteName">The name of the test suite.</param>
/// <param name="TestCount">The total number of tests in the suite.</param>
/// <param name="Tags">Optional tags or labels associated with the suite.</param>
/// <param name="ShardIndex">The 1-based shard index when this run is one shard of a larger suite; null for a whole-suite run.</param>
/// <param name="ShardTotal">The total number of shards when sharding; null for a whole-suite run.</param>
public sealed record TestSuiteInfo(
    string SuiteName,
    int TestCount,
    IReadOnlyList<string>? Tags = null,
    int? ShardIndex = null,
    int? ShardTotal = null);
