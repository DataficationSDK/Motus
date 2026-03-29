# Motus.Testing.NUnit

NUnit integration for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework.

## Overview

Provides `MotusTestBase`, a base class that launches a browser per fixture instance and creates an isolated `IBrowserContext` and `IPage` per test. Compatible with `[Parallelizable(ParallelScope.All)]`. Failure tracing is built in and captures a trace ZIP when a test fails, controlled by `motus.config.json` or the `MOTUS_FAILURES_TRACE` environment variable.

## Installation

```shell
dotnet add package Motus.Testing.NUnit
```

## Quick Start

```csharp
using Motus.Testing.NUnit;

[TestFixture]
public class SearchTests : MotusTestBase
{
    [Test]
    public async Task SearchBox_AcceptsInput()
    {
        await Page.GotoAsync("https://example.com");
        await Page.Locator("[name=q]").FillAsync("motus");

        var value = await Page.Locator("[name=q]").InputValueAsync();
        Assert.That(value, Is.EqualTo("motus"));
    }
}
```

### Customization

Override `LaunchOptions` or `ContextOptions` to configure the browser or viewport:

```csharp
protected override LaunchOptions LaunchOptions => new() { Headless = false };
protected override ContextOptions ContextOptions => new() { Viewport = new ViewportSize(1920, 1080) };
```
