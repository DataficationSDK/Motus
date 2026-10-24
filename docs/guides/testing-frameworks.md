# Test Framework Integration

Motus ships first-class test helpers for MSTest, NUnit, and xUnit. Each package follows the same principle: one browser per lifecycle scope, one isolated `IBrowserContext` per test (per test class by default under xUnit). Failure tracing is built in across all three frameworks and requires no boilerplate beyond enabling it in configuration.

---

## MSTest

Package: `Motus.Testing.MSTest` for MSTest 3, `Motus.Testing.MSTest.v4` for MSTest 4

### MSTest 3 and MSTest 4

MSTest 4 renamed the assemblies its types live in, and MSTest recognizes an inherited `[TestInitialize]` or `[TestCleanup]` only by the exact identity of its own attribute types. A base class built against one major is invisible to a test project on the other: its setup and cleanup never run, and a test that touches `TestContext` does not compile. Motus therefore ships the MSTest integration twice, built from the same source:

| Your test project uses | Reference |
|---|---|
| MSTest 4.x, which `dotnet new mstest` creates | `Motus.Testing.MSTest.v4` |
| MSTest 3.x | `Motus.Testing.MSTest` |

Both packages use the namespace `Motus.Testing.MSTest` and contain the same types, so moving a suite from MSTest 3 to 4 changes the package reference and nothing in the tests. Reference one or the other, not both. `Motus.Testing.MSTest` accepts MSTest 3 only, so a project on MSTest 4 that references it gets a `NU1608` warning at restore rather than tests that quietly never get a page.

### MotusTestBase

Inherit from `MotusTestBase` to receive a fully managed `IBrowserContext` and `IPage` for every test. A single browser is shared across the entire assembly and is stored in a static `BrowserFixture`. It launches when the first test needs it, so no assembly setup is required. Because each test gets its own context, the class is safe to use with `[Parallelize]`.

| Member | Description |
|---|---|
| `protected IBrowserContext Context` | The context for the current test. Available after `[TestInitialize]`. |
| `protected IPage Page` | The page for the current test. Available after `[TestInitialize]`. |
| `public TestContext TestContext` | Set by MSTest, and by `motus run` and the visual runner. Used internally to detect test outcome for failure tracing. |

### Browser lifecycle

The shared browser launches on first use. The first test to run pays for the launch inside its `[TestInitialize]`, and tests that start at the same moment under `[Parallelize]` wait for that one launch rather than starting browsers of their own. When the test process exits, the browser is closed and its temporary profile deleted.

There are three ways to set launch options, from broadest to narrowest:

1. **`motus.config.json`** (or `MOTUS_*` environment variables). The `launch` section applies to every browser the run starts, with no code. See the [Configuration Reference](./configuration.md).
2. **`[AssemblyInitialize]`**, for options set in code for the whole assembly, such as `Performance` or a computed `Args` list. Call `LaunchBrowserAsync` there to launch the shared browser up front with those options:

   ```csharp
   [TestClass]
   public static class TestSetup
   {
       [AssemblyInitialize]
       public static async Task AssemblyInitialize(TestContext _)
           => await MotusTestBase.LaunchBrowserAsync(new LaunchOptions
           {
               Performance = new PerformanceOptions { Enable = true },
           });

       [AssemblyCleanup]
       public static async Task AssemblyCleanup()
           => await MotusTestBase.CloseBrowserAsync();
   }
   ```

   `LaunchBrowserAsync` replaces any browser already running, so call it only from `[AssemblyInitialize]`, before tests start. `[AssemblyCleanup]` with `CloseBrowserAsync` is optional too; use it when you want the browser closed at a known point rather than at process exit. Projects that already have both hooks keep working unchanged.
3. **The `LaunchOptions` override** on a test class, for a class that needs a different browser from the rest (see below).

A launch that fails is reported to every test that asks for the browser within the next 30 seconds without being attempted again, so a missing browser fails a run quickly instead of costing three launch attempts per test.

### Virtual overrides

Override either property on your test class to customize options.

| Override | Default |
|---|---|
| `protected virtual LaunchOptions? LaunchOptions` | `null` (uses the shared browser) |
| `protected virtual ContextOptions? ContextOptions` | `new ContextOptions { Viewport = new ViewportSize(1024, 768) }` |

`ContextOptions` applies to each test's context and costs nothing extra. `LaunchOptions` gives the class a browser of its own, launched with those options, alongside the shared one. That browser belongs to the class that declares the override and is shared by every class inheriting it, so a base class with one override yields one extra browser however many test classes derive from it. A class that overrides again gets another. Returning `null` uses the shared browser.

```csharp
public abstract class HeadedTests : MotusTestBase
{
    protected override LaunchOptions? LaunchOptions => new() { Headless = false, SlowMo = 100 };
}

[TestClass] public class CheckoutTests : HeadedTests { /* ... */ }  // these two share
[TestClass] public class CartTests : HeadedTests { /* ... */ }      // one headed browser
```

Each extra browser stays open until the process exits or `CloseBrowserAsync` runs, so prefer `motus.config.json` for settings the whole run shares.

### Complete example

```csharp
using Motus.Abstractions;
using Motus.Testing.MSTest;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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

### Retrying a lost browser

A browser that dies mid-test takes the verdict with it. Nothing was established about the page, and what the run reports is a connection closing rather than anything the test set out to check. Browsers do occasionally die, more often on shared build hardware than on a desk, and a red build that a second run turns green is how a team learns to stop reading red builds.

Use `[MotusTestClass]` in place of `[TestClass]` to run each test in the class again when the browser goes away underneath it:

```csharp
[MotusTestClass]
public class CheckoutTests : MotusTestBase
{
    [TestMethod]
    public async Task CartTotalUpdatesAsync()
    {
        // Retried only if the browser disconnects.
    }
}
```

**Only a lost browser is retried.** A failed assertion is a result, and repeating it until it agrees would hide the very thing the suite exists to find. Browser loss is detected by a `MotusTargetClosedException` anywhere in the exception chain, not by a heuristic on the message.

The attempt that follows gets a browser of its own. The fixture starts replacing the dead one the moment it sees the disconnect, and `MotusTestBase` waits for that to finish while building the next context. A retry is announced on standard error in the same shape the Motus runner uses, so a test that passes only on a second attempt is visible in the log rather than silently green.

`Retries` defaults to 1 and can be raised on the class:

```csharp
[MotusTestClass(Retries = 2)]
```

`[MotusTestMethod]` applies the same behavior to a single method, with its own `Retries`. A method that carries it keeps its own setting even inside a `[MotusTestClass]`. It also has a constructor that wraps another `TestMethodAttribute`, so a custom test-method attribute keeps its behavior and only gains the retry.

This covers the narrow case of the browser vanishing. For retry policies, flake detection, and quarantine across a whole suite, use the `motus run` CLI and see [Flaky Tests and Quarantine](flaky-tests-and-quarantine.md).

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

xUnit does not support class-level setup hooks, so Motus uses the xUnit fixture system directly. Declare `BrowserContextFixture` as a class fixture and each test class gets its own context and page, with nothing else to set up.

### Quick start

```csharp
using Motus.Abstractions;
using Motus.Testing.xUnit;
using Xunit;

public class HomePageTests : IClassFixture<BrowserContextFixture>
{
    private readonly BrowserContextFixture _fixture;

    public HomePageTests(BrowserContextFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Title_ShouldContainAppName()
    {
        await _fixture.Page.GotoAsync("https://example.com");
        Assert.Contains("Example", await _fixture.Page.TitleAsync());
    }
}
```

There is no collection to declare and no `[Collection]` attribute to add.

### Browser lifecycle

Every `BrowserContextFixture` in the test process shares one browser. The first fixture to initialize launches it, and fixtures that start at the same moment wait for that one launch rather than starting browsers of their own. The browser is closed and its temporary profile deleted when the test process exits. xUnit has no assembly-level teardown, so there is no earlier point to close it at; if you need one, give a collection its own browser (see below).

Launch options for this browser come from `motus.config.json` or `MOTUS_*` environment variables. See the [Configuration Reference](./configuration.md).

Each test class gets one context and one page, created before its first test and closed after its last. The tests in a class share them. xUnit runs test classes in parallel by default, and each has its own context, so they do not see each other's cookies or storage. At most four contexts are open at once across the process; a class that would open a fifth waits for another to close.

A launch that fails is reported to every fixture that asks for the browser within the next 30 seconds without being attempted again, so a missing browser fails a run quickly.

### BrowserContextFixture

`BrowserContextFixture` is an `IAsyncLifetime` class fixture that creates a context and page once per test class.

| Member | Description |
|---|---|
| `BrowserContextFixture()` | Creates a fixture whose context comes from the browser shared by the test process. |
| `IBrowserContext Context` | The context for this test class. Available after `InitializeAsync`. |
| `IPage Page` | A page within the test class context. Available after `InitializeAsync`. |
| `protected virtual ContextOptions? ContextOptions` | Override to customize context options. Default is `null`. |

To change the context options, subclass it and declare the subclass as the class fixture:

```csharp
public class DesktopContextFixture : BrowserContextFixture
{
    protected override ContextOptions? ContextOptions => new()
    {
        Viewport = new ViewportSize(1920, 1080),
    };
}

public class DashboardTests : IClassFixture<DesktopContextFixture> { /* ... */ }
```

> **Note:** Automatic failure tracing is not supported with `BrowserContextFixture` because xUnit does not expose per-test outcome in `DisposeAsync`. For per-test traces, call `Context.Tracing.StartAsync` and `StopAsync` manually within your test methods.

### A context per test

When tests in one class must not share a context, create the fixture in the test class and drive it from the class's own `IAsyncLifetime`. xUnit creates a new instance of the class for every test, so every test gets a fresh context:

```csharp
public class CheckoutTests : IAsyncLifetime
{
    private readonly BrowserContextFixture _ctx = new();

    public Task InitializeAsync() => _ctx.InitializeAsync();
    public Task DisposeAsync() => _ctx.DisposeAsync();

    [Fact]
    public async Task Cart_StartsEmpty()
    {
        await _ctx.Page.GotoAsync("https://example.com/cart");
        // ...
    }
}
```

### A browser per collection

To give a group of test classes a browser of their own, separate from the shared one, put them in an xUnit collection with a `SharedBrowserFixture`. xUnit reads collection definitions only from the test assembly, so declare the collection in your test project. Deriving from `MotusCollection` makes it a one-liner:

```csharp
[CollectionDefinition("Checkout")]
public class CheckoutCollection : MotusCollection { }
```

Classes in the collection take their context from its browser with `BrowserContextFixture<SharedBrowserFixture>`. They can also receive the `SharedBrowserFixture` itself:

```csharp
[Collection("Checkout")]
public class CartTests : IClassFixture<BrowserContextFixture<SharedBrowserFixture>>
{
    private readonly BrowserContextFixture<SharedBrowserFixture> _fixture;

    public CartTests(BrowserContextFixture<SharedBrowserFixture> fixture) => _fixture = fixture;

    // ...
}
```

The collection's browser launches when the collection starts and closes when it finishes. Classes in one collection run one after another.

To launch that browser with options set in code, subclass `SharedBrowserFixture`, name the subclass in the collection, and use it as the type argument. xUnit matches fixtures by their exact type, so the class fixture must name the subclass:

```csharp
public class HeadedBrowser : SharedBrowserFixture
{
    protected override LaunchOptions? LaunchOptions => new() { Headless = false, SlowMo = 100 };
}

[CollectionDefinition("Headed")]
public class HeadedCollection : ICollectionFixture<HeadedBrowser> { }

[Collection("Headed")]
public class VisualTests : IClassFixture<BrowserContextFixture<HeadedBrowser>> { /* ... */ }
```

A class in a collection that declares plain `IClassFixture<BrowserContextFixture>` still uses the browser shared by the test process, not the collection's.

For a context per test from a collection's browser, receive the `SharedBrowserFixture` and create `new BrowserContextFixture<SharedBrowserFixture>(browser)` in the constructor, then drive it from `IAsyncLifetime` as shown above.

### SharedBrowserFixture

`SharedBrowserFixture` is an `IAsyncLifetime` collection fixture that manages a browser shared by the test classes of one collection.

| Member | Description |
|---|---|
| `IBrowser Browser` | The collection's browser. |
| `Task<IBrowserContext> NewContextAsync(ContextOptions?)` | Creates a new isolated context, launching the browser first if it has not been. |
| `Task CloseContextAsync(IBrowserContext)` | Closes a context from `NewContextAsync`. Use it rather than `context.CloseAsync()`, which leaves the context counted against the four allowed open at once. |
| `protected virtual LaunchOptions? LaunchOptions` | Override to customize launch options. |

### `BrowserContextFixture<TBrowser>`

`BrowserContextFixture<TBrowser>` is a `BrowserContextFixture` whose context comes from the collection fixture `TBrowser`, which is `SharedBrowserFixture` or a subclass of it.

| Member | Description |
|---|---|
| `BrowserContextFixture(TBrowser browser)` | Creates a fixture whose context comes from the given shared browser. xUnit supplies `browser` from the collection. |
| `TBrowser SharedBrowser` | The collection fixture that provides the browser. |

### MotusCollection

`MotusCollection` is a collection definition that provides a `SharedBrowserFixture`. xUnit ignores collection definitions outside the test assembly, so it serves as the base for one you declare, as shown above.

A class marked `[Collection(nameof(MotusCollection))]` with no such definition in your project joins an ordinary collection of that name: its classes run one after another, and `BrowserContextFixture` in them uses the browser shared by the test process. A class that also declares `IClassFixture<BrowserContextFixture>` runs as it is; the attribute can be removed to let it run in parallel with other classes. A class that only receives `BrowserContextFixture` in its constructor, without declaring the interface, still fails to start, and needs `IClassFixture<BrowserContextFixture>` added.

### Upgrading from 1.1

In 1.1, `BrowserContextFixture` had a public constructor that took a `SharedBrowserFixture`, and the xUnit examples created the fixture with `new BrowserContextFixture(browser)`. xUnit builds a class fixture through its only public constructor, and that is now the parameterless one, so that `IClassFixture<BrowserContextFixture>` works with nothing declared. The `SharedBrowserFixture` constructor is `protected` as a result. A call to it no longer builds (error CS0122, inaccessible due to its protection level), and a test assembly compiled against 1.1 fails with `MethodAccessException` when it runs against this release. Create the generic fixture instead:

```csharp
// 1.1
_ctx = new BrowserContextFixture(browser);

// Now
_ctx = new BrowserContextFixture<SharedBrowserFixture>(browser);
```

`BrowserContextFixture<SharedBrowserFixture>` derives from `BrowserContextFixture`, so a field or parameter declared as `BrowserContextFixture` can keep its type. A subclass that passes the browser to `base(browser)` builds unchanged.

The 1.1 examples also marked the class `[Collection(nameof(MotusCollection))]` to receive the `SharedBrowserFixture`. That ran only in a project that declared a collection definition of its own, because xUnit ignores the one in the package; keep that definition, or replace it with the one-line `MotusCollection` subclass under [A browser per collection](#a-browser-per-collection). If the class needs no browser of its own, drop the constructor parameter and create the fixture with `new()`, as under [A context per test](#a-context-per-test).

A class in such a collection that declares `IClassFixture<BrowserContextFixture>` received the collection's browser in 1.1 and now takes its context from the browser shared by the test process, launched with the same settings. To keep it on the collection's browser, declare `IClassFixture<BrowserContextFixture<SharedBrowserFixture>>` and receive that type in the constructor.

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

### Artifact names

Traces saved by the MSTest and NUnit base classes are named after the test that failed, using the UTC time at teardown:

```
trace-{Class}.{Method}-yyyyMMdd-HHmmss-fff.zip
trace-SaveReplaceDialogTests.EnterOnCancel-20261004-221501-123.zip
```

`{Class}` is the short name of the test class that ran the test, so a test method inherited from a shared base class is named for the derived class. Under NUnit, `{Method}` includes the arguments of a parameterized case.

The name is cleaned so the same file can be written, zipped, and uploaded as a CI artifact on any OS:

- Letters, digits, `.`, `-`, and `_` are kept. Every other character (slashes, colons, quotes, angle brackets, spaces, and so on) becomes `_`, and runs of `_` collapse to one.
- A name longer than 100 bytes is shortened and given a short hash of the full name, so two long names that share a prefix still produce different files.
- When a file of the same name already exists, for example two data rows of one test failing in the same millisecond, the new file gets a `-2`, `-3`, ... suffix instead of overwriting the first.

`FailureTracing` used without a test name keeps the timestamp-only form, `trace-yyyyMMdd-HHmmss-fff.zip`.

### Attached to the test result

When a test fails, the MSTest and NUnit base classes attach the saved trace and any failure screenshots to the test's result (`TestContext.AddResultFile` in MSTest, `TestContext.AddTestAttachment` in NUnit) and write the saved path to the test output:

```
Motus: failure trace saved to /src/app/test-results/traces/trace-CheckoutTests.PaysByCard-20261004-221501-123.zip
```

The attachments appear next to the failed test in the IDE's test explorer and in TRX results, so there is no need to match timestamps against a folder. Nothing is attached for a passing test.

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

A screenshot is taken when a locator action fails. Inside the MSTest and NUnit base classes it is named after the test in the same way as a trace, `failure-{Class}.{Method}-yyyyMMdd-HHmmss-fff.png`, and attached to the result when the test fails. Outside a test the name is `failure-yyyyMMdd-HHmmss-fff.png`.

### xUnit note

`FailureTracing` is not used automatically in the xUnit fixtures because xUnit does not provide test outcome at the class fixture teardown boundary. Use it manually within a test method if you need the same behavior, passing the test's name so the artifacts are named after it:

```csharp
[Fact]
public async Task Checkout_PaysByCard()
{
    var tracing = new FailureTracing($"{nameof(CheckoutTests)}.{nameof(Checkout_PaysByCard)}");
    await tracing.StartIfEnabledAsync(_ctx.Context);
    var failed = true;
    try
    {
        // ... test body ...
        failed = false;
    }
    finally
    {
        await tracing.StopAsync(_ctx.Context, failed);
        // tracing.TracePath and tracing.ScreenshotPaths hold what was saved.
    }
}
```

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

Or launch on first use, from each test's setup:

```csharp
await fixture.EnsureInitializedAsync(options); // launches once; later calls return at once
IBrowserContext ctx = await fixture.NewContextAsync(contextOptions);
```

Key behaviors:

- **Launch retry.** `InitializeAsync` retries browser launch up to three times with increasing delays (1 s, 2 s). This avoids flaky failures on CI runners where antivirus software can delay process creation.
- **Lazy launch.** `EnsureInitializedAsync` launches only when the fixture has no browser. Concurrent callers share one launch, a call during a crash restart waits for that restart rather than starting a second browser, and a call after `DisposeAsync` launches again. A failed launch is rethrown to callers for 30 seconds without another attempt.
- **Replacing the browser.** Calling `InitializeAsync` on a fixture that already has a browser closes that browser and launches a new one with the given options. Call it before tests start, not while they are using the current browser.
- **Auto-restart on crash.** The fixture subscribes to `IBrowser.Disconnected`. When Chrome crashes or becomes unresponsive (detected by the heartbeat or `Process.Exited`), the fixture automatically disposes the dead browser and launches a replacement with retry. The test that was running when the crash occurred fails normally; subsequent tests proceed against the fresh browser.
- **Restart coordination.** A `SemaphoreSlim` gate ensures that `NewContextAsync` callers block briefly if a restart is in progress rather than racing against a dead browser. This is transparent to test code and works correctly with parallel test execution (`[Parallelize]`, `[Parallelizable]`, and xUnit parallel collections).
- `Browser` throws `InvalidOperationException` if accessed before `InitializeAsync` completes.
- `DisposeAsync` unsubscribes from browser events, closes, and releases the browser process.
- **Process exit.** A browser the fixture launched is killed and its temporary profile deleted when the process exits, if `DisposeAsync` was never called. Test hosts can end the process within a fraction of a second of exit starting, so this is a safety net; dispose the fixture when you have a natural point to do it.

---

## See Also

- [Configuration Reference](./configuration.md) -- full `motus.config.json` schema including the `failure` section
- [Network Interception](./network-interception.md) -- route matching, request mocking, and response modification
- [Flaky Tests and Quarantine](./flaky-tests-and-quarantine.md) -- retry policies, flake detection, and quarantining a known-bad test
- [Sharding](./sharding.md) -- splitting a suite across CI agents and merging the results
