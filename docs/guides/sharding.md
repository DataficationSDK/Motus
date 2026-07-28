# Sharding

A browser suite is slow because browsers are slow, not because the machine is busy. Past a certain size the only way to shorten a run is to put it on more machines at once. Sharding splits a suite into disjoint slices so several `motus run` processes, typically one per CI agent, each execute a fraction of it. When they finish, `motus shard merge` recombines their result files into a single report.

No agent needs to know what the others took. The partition is a pure function of the discovered test set and the shard coordinates, so every process computes the same answer independently and no coordination step is required.

## Running one shard

Pass `--shard <index>/<total>` to `motus run`. The index is 1-based.

```bash
motus run bin/Release/net8.0/MyTests.dll --shard 1/4
```

Each shard writes its own result file. Give them distinct names, because the merge step reads them back from disk:

```bash
motus run bin/Release/net8.0/MyTests.dll \
  --shard 1/4 \
  --reporter console \
  --reporter junit:results.shard-1.xml
```

A malformed spec fails immediately with a message naming the expected form rather than running the whole suite. `--shard 0/4` and `--shard 5/4` are both rejected, since the index is 1-based and must fall inside the total.

## How tests are assigned

Discovered tests are sorted by a stable key, the assembly path followed by the fully qualified test name, compared ordinally. Reflection does not guarantee a consistent enumeration order across machines or runtime versions, so sorting first is what makes the partition reproducible rather than merely deterministic on one box.

The sorted list is then dealt out round-robin: position `i` belongs to shard `i % total`. Round-robin over a stable sort matters more than it looks. Tests from one class sort together, and a class that is slow is usually slow throughout, so handing out contiguous blocks would drop an entire slow class onto one agent. Dealing alternately spreads those tests across every shard without needing any timing data to do it.

The consequence worth internalizing: **shards are balanced by count, not by duration.** If one test takes four minutes and the rest take one second, the shard holding it finishes last no matter how many shards there are. Sharding shortens a suite made of many comparable tests. It does not rescue a suite dominated by one slow test.

## Merging the results

```bash
motus shard merge results.shard-*.xml --output junit:results.xml
```

The command takes the per-shard files as arguments and accepts JUnit or TRX. If your shell does not expand the glob, it is passed through and expanded internally. `--output` takes `junit:<path>` or `trx:<path>` and may be repeated to write more than one format. Omit it to print only the console summary.

Merging sums the buckets across files, including the flaky and quarantined counts described in [Flaky Tests and Quarantine](flaky-tests-and-quarantine.md). The merge exits non-zero when any test failed, when validation fails, or when no files were read.

### Catching a shard that never ran

This is the failure sharding introduces, and it is worth guarding explicitly. If an agent dies before writing its file, the merge sees three files instead of four and reports a smaller, entirely green suite. Nothing about that output looks wrong.

`--expect` closes it:

```bash
motus shard merge results.shard-*.xml --output junit:results.xml --expect 4
```

Each shard stamps its coordinates into its own result file when it writes it, as `motus.shard.index` and `motus.shard.total` properties. The merge reads them back and fails if a shard is missing or if the same index appears twice. Duplicates matter as much as absences: a retried agent that writes over the wrong filename would otherwise double-count its tests.

Use `--expect` in CI. A missing shard is indistinguishable from a passing run without it.

## Configuration

Shard coordinates can come from configuration instead of the command line, which is convenient when the CI system already exposes the agent index as an environment variable.

```json
{
  "shard": {
    "index": 1,
    "total": 4
  }
}
```

| Variable | Type | Description |
|---|---|---|
| `MOTUS_SHARD_INDEX` | int | 1-based shard index |
| `MOTUS_SHARD_TOTAL` | int | Total number of shards |

`--shard` takes precedence over both. The CLI form is a single `index/total` spec, while the configuration form carries the two values separately; both must be present in configuration for sharding to take effect.

## A complete CI example

The pattern is a matrix job that shards, followed by a single job that merges.

```yaml
jobs:
  test:
    strategy:
      fail-fast: false
      matrix:
        shard: [1, 2, 3, 4]
    steps:
      - run: |
          motus run bin/Release/net8.0/MyTests.dll \
            --shard ${{ matrix.shard }}/4 \
            --reporter junit:results.shard-${{ matrix.shard }}.xml
      - uses: actions/upload-artifact@v4
        with:
          name: results-${{ matrix.shard }}
          path: results.shard-*.xml

  merge:
    needs: test
    if: always()
    steps:
      - uses: actions/download-artifact@v4
      - run: motus shard merge **/results.shard-*.xml --output junit:results.xml --expect 4
```

Two details carry weight here. `fail-fast: false` lets every shard finish even after one fails, so a single failure still yields a complete picture rather than cancelling its siblings. `if: always()` runs the merge even when a shard job failed, which is exactly when you most want the combined report.

## See Also

- [Flaky Tests and Quarantine](flaky-tests-and-quarantine.md) -- retries, flake detection, and the counts the merge sums
- [Configuration](configuration.md) -- the full `motus.config.json` schema and environment variables
- [Test Framework Integration](testing-frameworks.md) -- fixtures and per-test isolation
