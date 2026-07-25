# Motus.Testing.MSTest

MSTest integration for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework.

## Overview

Provides `MotusTestBase`, a base class that shares a single browser across all tests in the assembly and creates an isolated `IBrowserContext` and `IPage` per test. Compatible with `[Parallelize]`. Failure tracing is built in and captures a trace ZIP when a test fails, controlled by `motus.config.json` or the `MOTUS_FAILURES_TRACE` environment variable.

`[MotusTestClass]` stands in for `[TestClass]` and runs a test again if the browser goes away underneath it.

## Installation

```shell
dotnet add package Motus.Testing.MSTest
```

## Quick Start

```csharp
using Motus.Testing.MSTest;

[MotusTestClass]
public class SearchTests : MotusTestBase
{
    [AssemblyInitialize]
    public static async Task Setup(TestContext _) => await LaunchBrowserAsync();

    [AssemblyCleanup]
    public static async Task Cleanup() => await CloseBrowserAsync();

    [TestMethod]
    public async Task SearchBox_AcceptsInput()
    {
        await Page.GotoAsync("https://example.com");
        await Page.Locator("[name=q]").FillAsync("motus");

        var value = await Page.Locator("[name=q]").InputValueAsync();
        Assert.AreEqual("motus", value);
    }
}
```

### Recovering from a lost browser

A browser that dies mid-test takes the verdict with it. Nothing was established about the page, and what the run reports is a connection closing rather than anything the test set out to check. `[MotusTestClass]` runs such a test again, against the replacement browser the fixture has already started:

```csharp
[MotusTestClass]                    // one further attempt, the default
[MotusTestClass(Retries = 2)]       // two
[MotusTestClass(Retries = 0)]       // none; each test runs once, whatever happens
```

Only a lost browser is retried. A failed assertion is a result, and repeating it until it agrees would hide what the suite exists to find. A retry announces itself on standard error as `[RETRY]`, so a test that only passes on a second attempt stays visible.

Individual methods can ask for it instead, with `[MotusTestMethod]` in place of `[TestMethod]`.

The `motus run` CLI has the same idea with more reach: `--retries` with `--retry-policy transient` (the default policy, disconnects only) or `flake` (any failure), plus quarantine lists and flake history.

### Customization

Override `LaunchOptions` or `ContextOptions` to configure the browser or viewport:

```csharp
protected override LaunchOptions LaunchOptions => new() { Headless = false };
protected override ContextOptions ContextOptions => new() { Viewport = new ViewportSize(1920, 1080) };
```
