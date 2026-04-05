# Test Framework Integration

Motus ships first-class test helpers for MSTest, NUnit, and xUnit. Each package follows the same principle: one browser per lifecycle scope, one isolated `IBrowserContext` per test. Failure tracing is built in across all three frameworks and requires no boilerplate beyond enabling it in configuration.

---

## MSTest

Package: `Motus.Testing.MSTest`

### MotusTestBase

Inherit from `MotusTestBase` to receive a fully managed `IBrowserContext` and `IPage` for every test. A single browser is shared across the entire assembly and is stored in a static `BrowserFixture`. Because each test gets its own context, the class is safe to use with `[Parallelize]`.

| Member | Description |
|---|---|
| `protected IBrowserContext Context` | The context for the current test. Available after `[TestInitialize]`. |
| `protected IPage Page` | The page for the current test. Available after `[TestInitialize]`. |
| `public TestContext TestContext` | Set by the MSTest runner. Used internally to detect test outcome for failure tracing. |

### Assembly lifecycle

The shared browser is not launched automatically. You must wire it up with `[AssemblyInitialize]` and `[AssemblyCleanup]` in a static class within your test assembly. Both methods are `static` on `MotusTestBase`.

```csharp
[TestClass]
public static class TestSetup
{
    [AssemblyInitialize]
    public static async Task AssemblyInitialize(TestContext _)
        => await MotusTestBase.LaunchBrowserAsync();

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
        => await MotusTestBase.CloseBrowserAsync();
}
```

You can pass a `LaunchOptions` instance to `LaunchBrowserAsync` if you need to override launch settings at the assembly level.

### Virtual overrides

Override either property on your test class to customize options without changing the shared browser setup.

| Override | Default |
|---|---|
| `protected virtual LaunchOptions? LaunchOptions` | `null` (uses config defaults) |
| `protected virtual ContextOptions? ContextOptions` | `new ContextOptions { Viewport = new ViewportSize(1024, 768) }` |

### Complete example

```csharp
using Motus.Abstractions;
using Motus.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public static class TestSetup
{
    [AssemblyInitialize]
    public static async Task AssemblyInitialize(TestContext _)
        => await MotusTestBase.LaunchBrowserAsync();

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
        => await MotusTestBase.CloseBrowserAsync();
}

[TestClass]
public class HomePageTests : MotusTestBase
{
    protected override ContextOptions? ContextOptions => new()
    {
        Viewport = new ViewportSize(1920, 1080),
    };

    [TestMethod]
    public async Task Title_ShouldContainAppName()
    {
        await Page.GotoAsync("https://example.com");
        StringAssert.Contains(await Page.TitleAsync(), "Example");
    }
}
```

---

## NUnit

Package: `Motus.Testing.NUnit`

### MotusTestBase

Inherit from `MotusTestBase` to receive a browser, context, and page. Each fixture class gets its own `BrowserFixture` instance (browser per fixture), and each test gets an isolated context and page. The class is compatible with `[Parallelizable(ParallelScope.All)]`.

| Member | Description |
|---|---|
| `protected IBrowser Browser` | The browser for this fixture. Available after `[OneTimeSetUp]`. |
| `protected IBrowserContext Context` | The context for the current test. Available after `[SetUp]`. |
| `protected IPage Page` | The page for the current test. Available after `[SetUp]`. |

### Per-fixture lifecycle

The base class handles all lifecycle hooks automatically.

| Hook | Action |
|---|---|
| `[OneTimeSetUp]` | Launches the browser via the fixture's `BrowserFixture`. |
| `[SetUp]` | Creates a new context and page; starts failure tracing if enabled. |
| `[TearDown]` | Saves or discards the trace; closes the context. |
| `[OneTimeTearDown]` | Disposes the browser. |

### Virtual overrides

| Override | Default |
|---|---|
| `protected virtual LaunchOptions? LaunchOptions` | `null` (uses config defaults) |
| `protected virtual ContextOptions? ContextOptions` | `new ContextOptions { Viewport = new ViewportSize(1024, 768) }` |

### Complete example

```csharp
using Motus.Abstractions;
using Motus.Testing.NUnit;
using NUnit.Framework;

[Parallelizable(ParallelScope.All)]
[TestFixture]
public class HomePageTests : MotusTestBase
{
    protected override ContextOptions? ContextOptions => new()
    {
        Viewport = new ViewportSize(1920, 1080),
    };

    [Test]
    public async Task Title_ShouldContainAppName()
    {
        await Page.GotoAsync("https://example.com");
        Assert.That(await Page.TitleAsync(), Does.Contain("Example"));
    }
}
```

---

## xUnit

Package: `Motus.Testing.xUnit`

xUnit does not support class-level setup hooks, so Motus uses the xUnit fixture system directly. Two fixture types cover the common scenarios.

### SharedBrowserFixture

`SharedBrowserFixture` is an `IAsyncLifetime` collection fixture that manages a single browser instance shared across all test classes in a collection.

| Member | Description |
|---|---|
| `IBrowser Browser` | The shared browser instance. |
| `Task<IBrowserContext> NewContextAsync(ContextOptions?)` | Creates a new isolated context from the shared browser. |
| `protected virtual LaunchOptions? LaunchOptions` | Override to customize launch options. |

### BrowserContextFixture

`BrowserContextFixture` is an `IAsyncLifetime` class fixture that creates a context and page once per test class. It requires `SharedBrowserFixture` via constructor injection.

| Member | Description |
|---|---|
| `IBrowserContext Context` | The context for this test class. Available after `InitializeAsync`. |
| `IPage Page` | A page within the test class context. Available after `InitializeAsync`. |
| `protected virtual ContextOptions? ContextOptions` | Override to customize context options. Default is `null`. |

> **Note:** Automatic failure tracing is not supported with `BrowserContextFixture` because xUnit does not expose per-test outcome in `DisposeAsync`. For per-test traces, call `Context.Tracing.StartAsync` and `StopAsync` manually within your test methods.

### MotusCollection

`MotusCollection` is a pre-built `[CollectionDefinition]` that wires `SharedBrowserFixture` as a collection fixture. Decorate your test class with `[Collection(nameof(MotusCollection))]` to participate.

```csharp
// Provided by the package -- no need to redeclare unless you subclass SharedBrowserFixture.
[CollectionDefinition(nameof(MotusCollection))]
public class MotusCollection : ICollectionFixture<SharedBrowserFixture>
{
}
```

If you subclass `SharedBrowserFixture` to override `LaunchOptions`, declare a new collection definition referencing your subclass instead of `MotusCollection`.

### Constructor injection

A test class in the collection receives `SharedBrowserFixture` and optionally declares `BrowserContextFixture` as a class fixture:

```csharp
[Collection(nameof(MotusCollection))]
public class HomePageTests : IClassFixture<BrowserContextFixture>, IAsyncLifetime
{
    private readonly BrowserContextFixture _ctx;

    public HomePageTests(SharedBrowserFixture browser)
    {
        _ctx = new BrowserContextFixture(browser);
    }

    public Task InitializeAsync() => _ctx.InitializeAsync();
    public Task DisposeAsync() => _ctx.DisposeAsync();
    ...
}
```

### Complete example

```csharp
using Motus.Abstractions;
using Motus.Testing.xUnit;
using Xunit;

[Collection(nameof(MotusCollection))]
public class HomePageTests : IAsyncLifetime
{
    private readonly BrowserContextFixture _ctx;

    public HomePageTests(SharedBrowserFixture browser)
    {
        _ctx = new BrowserContextFixture(browser);
    }

    public Task InitializeAsync() => _ctx.InitializeAsync();
    public Task DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Title_ShouldContainAppName()
    {
        await _ctx.Page.GotoAsync("https://example.com");
        Assert.Contains("Example", await _ctx.Page.TitleAsync());
    }
}
```

---

## Failure Tracing

`FailureTracing` (in `Motus.Testing`) is used internally by the MSTest and NUnit base classes to record a Playwright trace zip only when a test fails. The trace includes screenshots and DOM snapshots captured continuously throughout the test.

### What gets captured

When a test fails and tracing is enabled, `FailureTracing` calls `context.Tracing.StopAsync` with a file path and saves a `.zip` archive. The archive contains:

- Screenshots at each recorded action
- DOM snapshots (HTML + computed styles) at each action
- Network request and response metadata

The zip can be opened in the Playwright Trace Viewer (`npx playwright show-trace <file>`).

When the test passes, the in-memory trace is discarded without writing to disk.

### Enabling failure tracing

Failure tracing is opt-in. Enable it in `motus.config.json`:

```json
{
  "failure": {
    "trace": true,
    "tracePath": "test-results/traces"
  }
}
```

Or set environment variables:

| Variable | Type | Description |
|---|---|---|
| `MOTUS_FAILURES_TRACE` | `true` / `false` / `1` / `0` | Enables or disables trace capture on failure. |
| `MOTUS_FAILURES_TRACE_PATH` | string | Directory where trace zips are written. |

When `tracePath` / `MOTUS_FAILURES_TRACE_PATH` is not set, traces are written to `test-results/traces` by default. The directory is created automatically if it does not exist.

Trace file names follow the pattern `trace-yyyyMMdd-HHmmss-fff.zip` using the UTC time at teardown.

### Screenshot capture

The failure section also supports standalone screenshot capture, independently of trace:

| Variable | Type | Description |
|---|---|---|
| `MOTUS_FAILURES_SCREENSHOT` | `true` / `false` / `1` / `0` | Enables screenshot capture on failure. |
| `MOTUS_FAILURES_SCREENSHOT_PATH` | string | Directory where screenshots are written. |

In `motus.config.json`:

```json
{
  "failure": {
    "screenshot": true,
    "screenshotPath": "test-results/screenshots",
    "trace": true,
    "tracePath": "test-results/traces"
  }
}
```

### xUnit note

`FailureTracing` is not used automatically in the xUnit fixtures because xUnit does not provide test outcome at the class fixture teardown boundary. Call `FailureTracing.StartIfEnabledAsync` and `StopAsync` manually within test methods if you need the same behavior.

---

## BrowserFixture

`BrowserFixture` (`Motus.Testing`) is the low-level helper used internally by all three framework adapters. Use it directly when building custom fixture infrastructure.

```csharp
var fixture = new BrowserFixture();
await fixture.InitializeAsync(options); // retries up to 3 times on transient launch failure
IBrowserContext ctx = await fixture.NewContextAsync(contextOptions);
// ...
await fixture.DisposeAsync();
```

Key behaviors:

- `InitializeAsync` retries browser launch up to three times with increasing delays (1 s, 2 s). This avoids flaky failures on CI runners where antivirus software can delay process creation.
- `Browser` throws `InvalidOperationException` if accessed before `InitializeAsync` completes.
- `DisposeAsync` closes and releases the browser process.

---

## See Also

- [Configuration Reference](../configuration.md) -- full `motus.config.json` schema including the `failure` section
- [LaunchOptions and ContextOptions](../api/options.md) -- all available properties
- Playwright Trace Viewer: `npx playwright show-trace <trace.zip>`
