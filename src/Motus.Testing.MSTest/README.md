# Motus.Testing.MSTest

MSTest integration for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework.

## Overview

Provides `MotusTestBase`, a base class that shares a single browser across all tests in the assembly and creates an isolated `IBrowserContext` and `IPage` per test. Compatible with `[Parallelize]`. Failure tracing is built in and captures a trace ZIP when a test fails, controlled by `motus.config.json` or the `MOTUS_FAILURES_TRACE` environment variable.

## Installation

```shell
dotnet add package Motus.Testing.MSTest
```

## Quick Start

```csharp
using Motus.Testing.MSTest;

[TestClass]
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

### Customization

Override `LaunchOptions` or `ContextOptions` to configure the browser or viewport:

```csharp
protected override LaunchOptions LaunchOptions => new() { Headless = false };
protected override ContextOptions ContextOptions => new() { Viewport = new ViewportSize(1920, 1080) };
```
