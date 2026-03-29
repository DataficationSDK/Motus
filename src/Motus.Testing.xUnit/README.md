# Motus.Testing.xUnit

xUnit integration for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework.

## Overview

Provides collection and class fixtures for xUnit's parallel execution model. `SharedBrowserFixture` manages a single browser per test collection, and `BrowserContextFixture` creates an isolated context and page per test class. Tests in the same collection share the browser but run in separate contexts.

## Installation

```shell
dotnet add package Motus.Testing.xUnit
```

## Quick Start

```csharp
using Motus.Testing.xUnit;

[Collection(nameof(MotusCollection))]
public class SearchTests : IClassFixture<BrowserContextFixture>
{
    private readonly BrowserContextFixture _fixture;

    public SearchTests(BrowserContextFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SearchBox_AcceptsInput()
    {
        await _fixture.Page.GotoAsync("https://example.com");
        await _fixture.Page.Locator("[name=q]").FillAsync("motus");

        var value = await _fixture.Page.Locator("[name=q]").InputValueAsync();
        Assert.Equal("motus", value);
    }
}
```

### Customization

Override `LaunchOptions` on `SharedBrowserFixture` to configure the browser:

```csharp
public class HeadedBrowserFixture : SharedBrowserFixture
{
    protected override LaunchOptions LaunchOptions => new() { Headless = false };
}
```

### Failure Tracing

Automatic failure tracing is not supported at the class-fixture level because xUnit does not expose per-test outcome in `DisposeAsync`. Use manual `Tracing.StartAsync` / `StopAsync` for trace capture.
