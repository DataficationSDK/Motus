# Motus.Analyzers

Roslyn analyzers and code fixes for [Motus](https://github.com/DataficationSDK/Motus) test projects. Catches common browser automation mistakes at compile time.

## Overview

These analyzers flag patterns that cause flaky or incorrect browser tests, with automated code fixes where possible. They run during normal compilation and appear as warnings or suggestions in your IDE.

### Diagnostics

| ID | Severity | Description | Code Fix |
|----|----------|-------------|----------|
| MOT001 | Warning | Async Motus call not awaited | Adds `await` |
| MOT002 | Warning | `Task.Delay` or `Thread.Sleep` used as implicit wait | Replaces with `WaitForLoadStateAsync` |
| MOT003 | Info | CSS selector is fragile (deeply nested, chained `:nth-child`, or auto-generated class names) | -- |
| MOT004 | Warning | `IBrowser` or `IBrowserContext` created but not disposed | Wraps in `await using` |
| MOT005 | Warning | Locator created but never acted upon | -- |
| MOT006 | Warning | Deprecated selector syntax used | -- |
| MOT007 | Warning | Navigation called without waiting for load state | -- |

## Installation

```shell
dotnet add package Motus.Analyzers
```

This package is automatically included when you reference [Motus](https://www.nuget.org/packages/Motus). Direct installation is only needed if you want the analyzers without the full engine.
