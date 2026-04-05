# Getting Started

Motus is a browser automation and testing framework for .NET that communicates directly with browsers over CDP and WebDriver BiDi, with no Node.js sidecar required. This guide walks through installation, writing your first test, and running it.

## Prerequisites

- .NET 8.0 SDK or later
- A test framework of your choice (MSTest, NUnit, or xUnit)

## Installation

Add the Motus core package and the integration for your test framework:

```bash
# Core package (always required)
dotnet add package Motus

# Pick one test framework integration
dotnet add package Motus.Testing.MSTest
dotnet add package Motus.Testing.NUnit
dotnet add package Motus.Testing.xUnit
```

Install the CLI tool and download a browser:

```bash
dotnet tool install --global Motus.Cli
motus install
```

`motus install` downloads Chromium by default. To install a different browser:

```bash
motus install --channel firefox
motus install --channel chrome
motus install --channel edge
```

Optionally, add the Roslyn analyzers to catch common mistakes at compile time:

```bash
dotnet add package Motus.Analyzers
```

## Your First Test

### MSTest

MSTest uses a shared browser across the assembly with one isolated context per test.

```csharp
// AssemblySetup.cs
using Motus.Abstractions;
using Motus.Testing.MSTest;

[TestClass]
public class AssemblySetup
{
    [AssemblyInitialize]
    public static async Task Initialize(TestContext _) =>
        await MotusTestBase.LaunchBrowserAsync(new LaunchOptions { Headless = true });

    [AssemblyCleanup]
    public static async Task Cleanup() =>
        await MotusTestBase.CloseBrowserAsync();
}
```

```csharp
// SearchTests.cs
using Motus.Testing.MSTest;
using static Motus.Assertions.Expect;

[TestClass]
public class SearchTests : MotusTestBase
{
    [TestMethod]
    public async Task PageHasTitle()
    {
        await Page.GotoAsync("https://example.com");

        await That(Page).ToHaveTitleAsync("Example Domain");
    }

    [TestMethod]
    public async Task ClickLink()
    {
        await Page.GotoAsync("https://example.com");

        await Page.GetByRole("link", "More information...").ClickAsync();

        await That(Page).ToHaveUrlAsync("*iana.org*");
    }
}
```

### NUnit

NUnit launches a browser per fixture and creates one context per test. No assembly-level setup is needed.

```csharp
using Motus.Testing.NUnit;
using static Motus.Assertions.Expect;

[TestFixture]
public class SearchTests : MotusTestBase
{
    [Test]
    public async Task PageHasTitle()
    {
        await Page.GotoAsync("https://example.com");

        await That(Page).ToHaveTitleAsync("Example Domain");
    }
}
```

### xUnit

xUnit uses a collection fixture for the shared browser and a class fixture for per-test context.

```csharp
using Motus.Testing.xUnit;
using static Motus.Assertions.Expect;

[Collection(nameof(MotusCollection))]
public class SearchTests : IClassFixture<BrowserContextFixture>
{
    private readonly BrowserContextFixture _fixture;

    public SearchTests(BrowserContextFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PageHasTitle()
    {
        await _fixture.Page.GotoAsync("https://example.com");

        await That(_fixture.Page).ToHaveTitleAsync("Example Domain");
    }
}
```

## Running Tests

Run tests with the standard `dotnet test` command:

```bash
dotnet test
```

Or use the Motus CLI for richer output and reporting:

```bash
motus run bin/Release/net8.0/MyTests.dll --reporter console
```

Add `--visual` to open the Blazor-based visual runner:

```bash
motus run bin/Release/net8.0/MyTests.dll --visual
```

### Headed Mode

By default Motus runs headless. To see the browser while developing tests, set `Headless = false` in your launch options or use an environment variable:

```bash
MOTUS_HEADLESS=false dotnet test
```

### Slow Motion

Slow down every operation to watch what the test is doing:

```bash
MOTUS_SLOWMO=500 dotnet test
```

## Core Concepts

### Locators

Locators are the primary way to find elements on a page. They auto-wait for elements to be actionable before performing operations.

```csharp
// CSS selector
Page.Locator("button.submit")

// Semantic selectors (preferred)
Page.GetByRole("button", "Submit")
Page.GetByText("Welcome")
Page.GetByLabel("Email address")
Page.GetByPlaceholder("Search...")
Page.GetByTestId("login-form")
```

Locators can be chained and filtered:

```csharp
Page.GetByRole("listitem")
    .Filter(new LocatorOptions { HasText = "Product A" })
    .GetByRole("button", "Add to cart")
    .ClickAsync();
```

### Actions

Common interactions on locators:

```csharp
await locator.ClickAsync();
await locator.FillAsync("hello@example.com");
await locator.SelectOptionAsync("medium");
await locator.CheckAsync();
await locator.PressAsync("Enter");
```

### Assertions

Assertions auto-retry until passing or a timeout is reached. Use `Expect.That()` or import it statically:

```csharp
using static Motus.Assertions.Expect;

// Page assertions
await That(Page).ToHaveTitleAsync("Dashboard");
await That(Page).ToHaveUrlAsync("**/dashboard");

// Locator assertions
await That(Page.GetByRole("heading")).ToHaveTextAsync("Welcome");
await That(Page.GetByRole("button", "Submit")).ToBeEnabledAsync();

// Negation
await That(Page.Locator(".spinner")).Not.ToBeVisibleAsync();
```

### Navigation

```csharp
await Page.GotoAsync("https://example.com");
await Page.GoBackAsync();
await Page.GoForwardAsync();
await Page.ReloadAsync();

// Wait for a specific URL after an action triggers navigation
await Page.WaitForURLAsync("**/dashboard");
```

## Configuration

Motus supports a layered configuration model: `motus.config.json` < environment variables < code. Create a `motus.config.json` in your project root for shared defaults:

```json
{
  "launch": {
    "headless": true,
    "channel": "chromium",
    "timeout": 30000
  },
  "assertions": {
    "timeout": 5000
  },
  "failure": {
    "screenshot": true,
    "screenshotPath": "test-results/screenshots"
  }
}
```

Environment variables use the `MOTUS_` prefix and override JSON values. Code-supplied `LaunchOptions` and `ContextOptions` take the highest precedence.

See [Configuration](guides/configuration.md) for the full schema reference.

## What's Next

- [Configuration](guides/configuration.md) -- full config schema and environment variables
- [Testing Frameworks](guides/testing-frameworks.md) -- deeper framework integration details
- [Network Interception](guides/network-interception.md) -- mock API responses and intercept requests
- [Accessibility Testing](guides/accessibility-testing.md) -- built-in WCAG 2.1 auditing
- [Recording and Code Generation](guides/recording-and-codegen.md) -- record browser sessions into test code
- [Plugin Extensibility](extensions/getting-started.md) -- build custom selectors, hooks, and reporters
- [Architecture Overview](architecture/overview.md) -- how Motus works under the hood
