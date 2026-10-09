# Motus.Testing.xUnit

xUnit integration for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework.

## Overview

Provides class fixtures for xUnit's parallel execution model. `BrowserContextFixture` gives each test class an isolated context and page, taken from one browser shared by the whole test process. That browser launches when the first class needs it and closes when the process exits, so there is nothing to declare beyond the class fixture. Test classes run in parallel, each in its own context.

## Installation

```shell
dotnet add package Motus.Testing.xUnit
```

## Quick Start

```csharp
using Motus.Testing.xUnit;

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

Launch options for the shared browser come from `motus.config.json` or `MOTUS_*` environment variables. Override `ContextOptions` on a `BrowserContextFixture` subclass to configure the context.

To give a group of classes a browser of their own, launched with options set in code, subclass `SharedBrowserFixture` and declare a collection for it in your test project (xUnit reads collection definitions only from the test assembly). Classes in the collection take their context from it with `BrowserContextFixture<T>`:

```csharp
public class HeadedBrowserFixture : SharedBrowserFixture
{
    protected override LaunchOptions LaunchOptions => new() { Headless = false };
}

[CollectionDefinition("Headed")]
public class HeadedCollection : ICollectionFixture<HeadedBrowserFixture> { }

[Collection("Headed")]
public class VisualTests : IClassFixture<BrowserContextFixture<HeadedBrowserFixture>>
{
    public VisualTests(BrowserContextFixture<HeadedBrowserFixture> fixture) { /* ... */ }
}
```

For the default options, `[CollectionDefinition("Name")] public class NameCollection : MotusCollection { }` declares the collection, and its classes use `BrowserContextFixture<SharedBrowserFixture>`.

### Failure Tracing

Automatic failure tracing is not supported at the class-fixture level because xUnit does not expose per-test outcome in `DisposeAsync`. Use manual `Tracing.StartAsync` / `StopAsync` for trace capture.
