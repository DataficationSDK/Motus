# Motus.Testing

Shared browser fixture for [Motus](https://github.com/DataficationSDK/Motus) test framework integrations.

## Overview

Provides the framework-agnostic `BrowserFixture` that manages browser launch, retry logic for transient CI failures, and context isolation. The three test framework packages (MSTest, xUnit, NUnit) build on top of this. Also includes `FailureTracing` for automatic trace capture on test failure.

### Components

| Class | Description |
|-------|-------------|
| `BrowserFixture` | Launches a browser with up to 3 retry attempts, auto-restarts on crash, exposes `NewContextAsync` for per-test isolation |
| `FailureTracing` | Starts tracing before a test and saves the trace ZIP to `test-results/traces/` only when the test fails |

### Failure Tracing Configuration

Tracing is enabled via `motus.config.json` (`failure.trace: true`) or the `MOTUS_FAILURES_TRACE` environment variable. The output path is configurable via `failure.tracePath`.

## Installation

```shell
dotnet add package Motus.Testing
```

This package depends on [Motus](https://www.nuget.org/packages/Motus) and [Motus.Abstractions](https://www.nuget.org/packages/Motus.Abstractions). Most consumers should install one of the framework-specific packages instead:

- [Motus.Testing.MSTest](https://www.nuget.org/packages/Motus.Testing.MSTest)
- [Motus.Testing.xUnit](https://www.nuget.org/packages/Motus.Testing.xUnit)
- [Motus.Testing.NUnit](https://www.nuget.org/packages/Motus.Testing.NUnit)
