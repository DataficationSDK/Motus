# Motus

Core browser automation engine for the [Motus](https://github.com/DataficationSDK/Motus) framework.

## Overview

Motus communicates directly with Chromium-based browsers over CDP and Firefox over WebDriver BiDi via WebSocket, with no Node.js sidecar or process boundary. Source-generated protocol bindings enable NativeAOT compatibility. The framework's own features are built on the same public plugin interfaces available to third-party authors.

### Features

- **Direct WebSocket communication** with Chromium (CDP) and Firefox (WebDriver BiDi)
- **Source-generated protocol bindings** for NativeAOT and trimming support
- **Plugin system** where all built-in selector strategies, lifecycle hooks, wait conditions, and reporters use the same `IPluginContext` as extensions
- **Browser pool** for parallel test execution with configurable concurrency
- **Automatic browser lifecycle** management including temp profile cleanup

## Installation

```shell
dotnet add package Motus
```

This package depends on [Motus.Abstractions](https://www.nuget.org/packages/Motus.Abstractions).

## Quick Start

```csharp
using Motus;

await using var browser = await MotusLauncher.LaunchAsync();
var page = await browser.NewPageAsync();

await page.GotoAsync("https://example.com");
await page.Locator("a").ClickAsync();
```

## Related Packages

| Package | Description |
|---------|-------------|
| [Motus.Abstractions](https://www.nuget.org/packages/Motus.Abstractions) | Plugin interfaces (for plugin authors) |
| [Motus.Codegen](https://www.nuget.org/packages/Motus.Codegen) | Source generator for CDP protocol bindings |
| [Motus.Analyzers](https://www.nuget.org/packages/Motus.Analyzers) | Roslyn analyzers for common automation mistakes |
| [Motus.Recorder](https://www.nuget.org/packages/Motus.Recorder) | Record browser interactions and emit test code |
| [Motus.Testing](https://www.nuget.org/packages/Motus.Testing) | Shared browser fixture for test frameworks |
| [Motus.Testing.MSTest](https://www.nuget.org/packages/Motus.Testing.MSTest) | MSTest integration |
| [Motus.Testing.xUnit](https://www.nuget.org/packages/Motus.Testing.xUnit) | xUnit integration |
| [Motus.Testing.NUnit](https://www.nuget.org/packages/Motus.Testing.NUnit) | NUnit integration |
| [Motus.Cli](https://www.nuget.org/packages/Motus.Cli) | CLI for test execution, recording, and browser management |
