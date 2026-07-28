# Flaky Tests and Quarantine

A browser test can fail for two quite different reasons: the thing it tests is broken, or the browser blinked. Treating those the same is what makes a suite untrustworthy. Retry everything and real bugs get hidden; retry nothing and a WebSocket drop fails the build.

Motus separates the two. Retries are governed by a policy that decides which failures are eligible, a test that only passes on a retry is reported as flaky rather than silently green, and a test known to be unreliable can be quarantined so it keeps running and reporting without gating the build.

## Retries and the retry policy

`--retries` sets how many extra attempts a failing test gets. `--retry-policy` decides which failures qualify.

```bash
motus run bin/Release/net8.0/MyTests.dll --retries 2 --retry-policy flake
```

| Policy | Retries | Use when |
|---|---|---|
| `transient` (default) | Only transient CDP failures: the browser disconnected or was lost | Always safe. A dropped connection is not a test result. |
| `flake` | Any failure, including assertion failures | You are hunting intermittent failures and want them labeled rather than hidden |

`transient` is the default because it cannot mask a bug. A browser that vanished mid-test tells you nothing about the code under test, so re-running is the only way to get an answer at all.

`flake` is the deliberate choice. It re-runs assertion failures too, which means a genuinely broken test that happens to pass on a second attempt is reported as flaky rather than failed. That is the point, but it is only useful if you then act on the flaky label, which is what `--fail-on-flaky` and the flake history exist for.

Every attempt rebuilds the test instance and its browser context, so a retry runs against clean state rather than inheriting whatever the failed attempt left behind. Data collected during a failed attempt is discarded, so accessibility violations and performance metrics reflect the attempt that counted.

## What counts as flaky

A test is flaky when it passed but needed more than one attempt. That definition has a deliberate edge: if the test body passed on a later attempt but an enforcing hook then failed the test, for example an accessibility audit in enforce mode, the result is a hard failure and not flaky. The test did not recover; it just failed later.

Each result carries `Attempts` alongside `Flaky` and `Quarantined`, so a report can show that a test passed on the third try rather than only that it eventually passed.

By default a flaky test passes the run and prints a warning. `--fail-on-flaky` makes the run exit non-zero instead:

```bash
motus run bin/Release/net8.0/MyTests.dll --retries 2 --retry-policy flake --fail-on-flaky
```

This is the setting that stops flakiness accumulating. Without it, a suite can drift into needing retries everywhere and still report success indefinitely.

## Quarantine

Quarantine is for a test you know is unreliable and are not going to fix this week. The alternatives are worse: deleting it loses the coverage, and skipping it makes it invisible until someone wonders why it never runs. A quarantined test still runs, still reports, and is counted in its own bucket, but its failures do not affect the exit code.

Mark it in code with `[Quarantine]`, on a method or on a whole class:

```csharp
[TestMethod]
[Quarantine(Reason = "Times out against the staging payment provider, see #412")]
public async Task CheckoutCompletesAsync()
{
    // ...
}
```

The attribute is inherited, so applying it to a base class quarantines the tests in derived classes too.

Or supply a list file, which quarantines without touching the source. That suits a suite you do not own, or a temporary measure you want gone from the branch cleanly:

```bash
motus run bin/Release/net8.0/MyTests.dll --quarantine quarantine.txt
```

One fully qualified test name per line. Blank lines are ignored, and a line whose first non-blank character is `#` is a comment. The whole line is the name, so a trailing `# reason` on the same line becomes part of it and the entry silently matches nothing; put the reason on its own line above.

```
# Flaky since the payment provider sandbox migration (#412)
MyTests.Checkout.CheckoutCompletesAsync

# Intermittent on Windows agents only (#487)
MyTests.Uploads.LargeFileUploadAsync
```

The two mechanisms combine rather than conflict: a test is quarantined if the attribute says so or the list names it. An unreadable list file is an error and stops the run, rather than being ignored into a build that silently gates on tests you meant to exclude.

## Flake history

A single run tells you a test was flaky once. What you usually need to know is whether it has been flaky for a month.

```bash
motus run bin/Release/net8.0/MyTests.dll --retries 2 --retry-policy flake --flaky-history flake-history.json
```

The file accumulates per-test counters across runs, merging with whatever is already there:

```json
{
  "MyTests.Checkout.CheckoutCompletesAsync": {
    "runs": 128,
    "failures": 3,
    "flakyPasses": 17,
    "lastSeenUtc": "2026-07-28T14:02:11Z"
  }
}
```

`runs` counts every recorded execution, `failures` counts runs that ended failed, and `flakyPasses` counts runs that passed only after a retry. A flake rate is `(failures + flakyPasses) / runs`, which is the number worth sorting by when deciding what to fix first.

A corrupt or unreadable history file is tolerated rather than fatal, since losing trend data should not fail a build. Persist the file between runs, as a CI cache or a committed artifact, or every run starts from nothing.

## How results are reported

The console, HTML, JUnit and TRX reporters all distinguish flaky and quarantined results, so the state survives into whatever reads the output. JUnit and TRX carry them as categories and outcomes their consumers already understand, which means a CI dashboard shows a quarantined failure as quarantined rather than as a passing test that mysteriously logged an error.

When sharding, `motus shard merge` sums the flaky and quarantined counts along with the rest. See [Sharding](sharding.md).

## Configuration

Every option has a configuration equivalent, so a policy can live in the repository rather than in each CI invocation.

```json
{
  "flaky": {
    "retryPolicy": "flake",
    "retries": 2,
    "failOnFlaky": true,
    "historyPath": "flake-history.json",
    "quarantinePath": "quarantine.txt"
  }
}
```

| Variable | Type | Description |
|---|---|---|
| `MOTUS_FLAKY_RETRY_POLICY` | string | `transient` or `flake` |
| `MOTUS_FLAKY_RETRIES` | int | Extra attempts for a failing test |
| `MOTUS_FLAKY_FAIL` | bool | Exit non-zero when any test is flaky |
| `MOTUS_FLAKY_HISTORY` | string | Path to the flake history file |
| `MOTUS_FLAKY_QUARANTINE` | string | Path to the quarantine list file |

Command-line options take precedence over both. Flake detection needs `--retries` of at least 1: setting the policy to `flake` with no retry budget leaves nothing to retry with.

## Browser loss under MSTest

The retry machinery above belongs to `motus run`. When tests are driven by the framework's own runner instead, MSTest offers the same protection for the narrow case of a browser disappearing. See [Test Framework Integration](testing-frameworks.md#retrying-a-lost-browser).

## See Also

- [Sharding](sharding.md) -- splitting a suite across agents and merging the results
- [Test Framework Integration](testing-frameworks.md) -- fixtures, per-test isolation, and browser-loss retries
- [Configuration](configuration.md) -- the full `motus.config.json` schema and environment variables
