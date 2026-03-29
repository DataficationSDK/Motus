# Motus.Recorder

Test session recorder for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework.

## Overview

The recorder captures browser interactions by injecting a lightweight script into the page via CDP, then emits compilable C# test code. It infers selectors from captured DOM events using a ranking strategy that prefers test IDs and accessible roles over fragile CSS paths. An optional Page Object Model emitter groups related form interactions into reusable classes.

### Captured Actions

- **Click**, **Fill**, **KeyPress**, **Check**, **Select** (form interactions)
- **Navigation** (URL changes)
- **Scroll**, **Dialog**, **FileUpload**

### Code Emission

- **Test code** via `CodeEmitter` targeting MSTest, xUnit, or NUnit
- **Page Object Models** via `PomEmitter` with automatic form field grouping
- Configurable class name, method name, namespace, and timing preservation

## Installation

```shell
dotnet add package Motus.Recorder
```

This package depends on [Motus](https://www.nuget.org/packages/Motus) and [Motus.Abstractions](https://www.nuget.org/packages/Motus.Abstractions).

## Quick Start

```csharp
using Motus;
using Motus.Recorder;

await using var browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = false });
var page = await browser.NewPageAsync();

var engine = new ActionCaptureEngine();
await engine.StartAsync(page);

// Interact with the browser manually...

await engine.StopAsync();

var emitter = new CodeEmitter();
var code = emitter.Emit(engine.CapturedActions, new CodeEmitOptions
{
    Framework = "mstest",
    TestClassName = "LoginTests"
});

await File.WriteAllTextAsync("LoginTests.cs", code);
```
