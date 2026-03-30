# Motus

**Extensible browser automation and testing framework for .NET, built on a fully extensible architecture.**

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE.md)
[![.NET 8 | 10](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-purple.svg)](https://dotnet.microsoft.com/download/dotnet/10.0)
[![NuGet](https://img.shields.io/nuget/v/Motus?label=NuGet)](https://www.nuget.org/packages/Motus)

<video src="https://github.com/user-attachments/assets/cb74ef25-23f6-4c43-9946-8219a634381f" autoplay loop muted playsinline></video>

## The Story

Every .NET browser automation framework either wraps a JavaScript tool behind a process boundary or bolts extensibility on as an afterthought. Playwright for .NET proxies commands through a Node.js sidecar. Selenium's extension model grew organically over fifteen years and it shows.

Motus started from a premise proven by the architecture of [Verso](https://github.com/DataficationSDK/Verso): if the framework's own features are built on the same public plugin interfaces available to third-party authors, the architecture stays honest. Every built-in selector strategy, lifecycle hook, wait condition, and reporter is registered through the same `IPluginContext` that any consumer can use. There are no internal shortcuts.

The result is a framework that talks directly to Chromium and Firefox over WebSocket (CDP and WebDriver BiDi), ships source-generated protocol bindings for NativeAOT, and gives you compile-time diagnostics for common automation mistakes before your tests ever run.

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Chrome, Edge, or Firefox installed (or use `motus install` to download a browser binary)

### Install

```bash
dotnet add package Motus
dotnet add package Motus.Testing.MSTest # or Motus.Testing.xUnit / Motus.Testing.NUnit
dotnet tool install --global Motus.Cli
```

### Write Your First Test

```csharp
using Motus;
using Motus.Abstractions;
using Motus.Testing.MSTest;
using static Motus.Assertions.Expect;

[TestClass]
public class SearchTests : MotusTestBase
{
    [TestMethod]
    public async Task SearchReturnsResults()
    {
        var page = await Context.NewPageAsync();
        await page.GotoAsync("https://example.com");

        await page.Locator("input[name='q']").FillAsync("motus automation");
        await page.Locator("button[type='submit']").ClickAsync();

        await That(page).ToHaveUrlAsync("**/search**");
        await That(page.Locator(".results")).ToBeVisibleAsync();
    }
}
```

### Generate Page Objects from Live Pages

```bash
motus codegen https://example.com/login --output ./Pages --namespace MyApp.Pages

# Or open a browser, navigate yourself, then press Enter to analyze
motus codegen --headed --output ./Pages
```

This navigates to the URL, crawls the DOM for interactive elements, infers the best selector for each using pluggable strategies, and emits a typed `.g.cs` Page Object Model class. Use `--detect-listeners` to also discover elements with directly-attached JS event handlers (vanilla JS, jQuery, etc.).

### Record a Test Session

```bash
motus record --output ./Tests/LoginTest.cs --framework mstest --selector-priority testid,role,text,css
```

### Run Tests

```bash
motus run --workers auto --reporter console
motus run --visual # launches the Blazor visual runner
motus run --reporter html:./reports/result.html
motus run --reporter junit:./reports/junit.xml
```

## How It Works

Motus communicates directly with the browser over WebSocket. For Chromium-based browsers, it speaks the Chrome DevTools Protocol (CDP). For Firefox, it uses WebDriver BiDi. There is no Node.js sidecar, no driver binary, and no process boundary between your test code and the protocol layer.

All CDP types are source-generated at build time from the protocol JSON schema. Serialization uses `System.Text.Json` source generators for zero-reflection, NativeAOT-compatible marshalling.

### Architecture

| Project | Role |
|---------|------|
| `Motus.Abstractions` | Public interfaces and types (zero dependencies) |
| `Motus` | Core engine: transport, browser management, page controller, locators, selectors, assertions |
| `Motus.Codegen` | Roslyn source generator for CDP protocol types and plugin discovery |
| `Motus.Analyzers` | Roslyn diagnostic analyzers and code fixes |
| `Motus.Recorder` | Action capture, selector inference, POM generation |
| `Motus.Runner` | Blazor visual test runner with live screencast and timeline |
| `Motus.Cli` | `motus` CLI tool |
| `Motus.Testing.*` | Test framework integrations (MSTest, xUnit, NUnit) |

## The Extension Model

Five interfaces define every point of extensibility, all registered through `IPluginContext`:

| Interface | What It Does |
|-----------|-------------|
| `ISelectorStrategy` | Resolve elements and generate selectors for a custom prefix (e.g. `data-testid=`) |
| `ILifecycleHook` | Intercept navigation, actions, page create/close, console messages, and errors |
| `IWaitCondition` | Define named wait conditions for use with `WaitForAsync` |
| `IReporter` | Receive test run events for custom reporting (multiple reporters run simultaneously) |
| `IMotusLogger` | Structured logging for plugin diagnostics |

### Plugin Discovery

Plugins are discovered two ways:

1. **Compile-time auto-discovery**: Mark a class with `[MotusPlugin]` and the Roslyn source generator emits a `[ModuleInitializer]` that registers it at startup. No reflection.
2. **Manual registration**: Pass instances via `LaunchOptions.Plugins` for full control.

```csharp
[MotusPlugin]
public class MyPlugin : IPlugin
{
    public string PluginId => "my-plugin";
    public string Name => "My Plugin";
    public string Version => "1.0.0";

    public Task OnLoadedAsync(IPluginContext context)
    {
        context.RegisterSelectorStrategy(new MyCustomStrategy());
        context.RegisterLifecycleHook(new MyHook());
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync() => Task.CompletedTask;
}
```

### Dogfooding All the Way Down

The five built-in selector strategies (CSS, XPath, Text, Role, TestId) are registered through `IPluginContext`. The console, HTML, JUnit, and TRX reporters implement `IReporter`. The visual runner's timeline is powered by an `ILifecycleHook`. None of them have special access to engine internals.

## Roslyn Analyzers

The `Motus.Analyzers` package ships seven diagnostics that catch common automation mistakes at compile time:

| Rule | Severity | What It Catches |
|------|----------|----------------|
| MOT001 | Warning | Non-awaited automation call |
| MOT002 | Info | Hardcoded `Task.Delay` or `Thread.Sleep` in test code |
| MOT003 | Info | Fragile selector (deep nesting, `nth-child` chains, auto-generated class names) |
| MOT004 | Warning | `IBrowser` or `IBrowserContext` not disposed |
| MOT005 | Warning | Locator created but never used |
| MOT006 | Info | Deprecated selector prefix |
| MOT007 | Warning | Assertion after navigation without intervening wait |

Code fixes are provided for MOT001, MOT002, and MOT004.

## Visual Runner

Launch the Blazor-based visual runner with `motus run --visual`:

- **Live browser view** with real-time screencast via CDP `Page.startScreencast`
- **Action timeline** with clickable steps, before/after screenshots, network requests, and console messages
- **Step-through debugging** with pause, inspect, and resume controls
- **Visual regression** with pixel-level diff, side-by-side comparison, and baseline management

## Key Features

| Category | Details |
|----------|---------|
| **Browser Support** | Chromium (CDP), Firefox (WebDriver BiDi) |
| **Selectors** | CSS, XPath, Text, ARIA Role, Test ID, plus custom strategies via `ISelectorStrategy` |
| **Auto-Wait** | Actionability checks (visible, enabled, stable, receives events) with configurable timeout |
| **Shadow DOM** | Automatic piercing of open shadow roots (configurable per-locator) |
| **Assertions** | Auto-retry polling assertions for locators, pages, and responses with `.Not` negation |
| **Network** | Request interception, response mocking, route matching with glob patterns |
| **Recording** | Capture browser interactions and emit idiomatic C# test code (MSTest, xUnit, NUnit) |
| **Codegen** | Generate Page Object Model classes from live pages with selector inference |
| **Reporters** | Console, HTML, JUnit XML, TRX, plus custom reporters via `IReporter` |
| **Tracing** | Screenshots, DOM snapshots, network logs, HAR export, and WebM video recording |
| **Parallel** | Context-level, browser-level, and worker-level parallel execution |
| **Configuration** | Layered: `motus.config.json`, environment variables, code (code always wins) |
| **NativeAOT** | Source-generated serialization and plugin discovery with zero reflection |

## CLI Reference

```
motus run              Run tests with optional --visual, --filter, --workers, --reporter
motus record           Record a browser session and emit test code
motus codegen          Generate POM classes from live pages (--headed, --connect, --detect-listeners)
motus screenshot       Capture a screenshot (--full-page, --delay, --hide-banners, --width, --height)
motus pdf              Generate a PDF from a URL (--delay, --hide-banners, --width, --timeout)
motus trace show       Open a trace file in the visual runner with timeline, screenshots, and network
motus install          Download and install browser binaries
motus update-protocol  Fetch and update CDP protocol schema files
```

## Build from Source

```bash
git clone https://github.com/DataficationSDK/Motus
cd Motus
dotnet build Motus.sln
dotnet test Motus.sln
```

## Contributing

Contributions are welcome. Open an issue to discuss what you'd like to work on.

## License

[MIT](LICENSE.md)

Motus is a Datafication project.
