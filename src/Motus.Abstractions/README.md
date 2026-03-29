# Motus.Abstractions

Pure interfaces and types for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework.

## Overview

This package contains every interface that defines the Motus API surface: the browser hierarchy, input devices, network layer, and the plugin system. Plugin authors reference **only this package**, with no dependency on the engine or any protocol implementation.

### Browser Hierarchy

| Interface | Purpose |
|-----------|---------|
| `IBrowser` | Top-level browser instance; create contexts and pages |
| `IBrowserContext` | Isolated session with its own cookies, cache, and storage |
| `IPage` | Single browser tab; navigation, locators, evaluation, screenshots |
| `IFrame` | Frame-level counterpart to `IPage` |
| `ILocator` | Element finder with action methods (`ClickAsync`, `FillAsync`, `TypeAsync`, etc.) |
| `IElementHandle` | Low-level reference to a DOM element |
| `IJSHandle` | Reference to a JavaScript object |

### Input and Network

| Interface | Purpose |
|-----------|---------|
| `IKeyboard` | Keyboard input simulation |
| `IMouse` | Mouse input with button and coordinate control |
| `ITouchscreen` | Touch gesture simulation |
| `IRequest` / `IResponse` | Network request and response inspection |
| `IRoute` | Intercept and modify network requests |
| `IDialog` | Handle JavaScript dialogs (alert, confirm, prompt) |
| `IDownload` | Track and save file downloads |
| `ITracing` | Chromium trace capture |

### Plugin System

| Interface | Purpose |
|-----------|---------|
| `IPlugin` | Base plugin interface with lifecycle hooks |
| `IPluginContext` | Registration point for selector strategies, wait conditions, lifecycle hooks, and reporters |
| `ISelectorStrategy` | Custom element selection logic |
| `IWaitCondition` | Custom wait-until conditions |
| `ILifecycleHook` | Intercept navigation, actions, page creation, console messages, and errors |
| `IReporter` | Custom test result reporting |

### Concurrency

| Interface | Purpose |
|-----------|---------|
| `IBrowserPool` | Pool of browser instances for parallel test execution |
| `IBrowserLease` | Scoped lease on a pooled browser |

## Installation

```shell
dotnet add package Motus.Abstractions
```

## Usage

```csharp
using Motus.Abstractions;

[MotusPlugin]
public class RetryClickHook : IPlugin, ILifecycleHook
{
    public string PluginId => "retry-click";
    public string Name => "Retry Click";
    public string Version => "1.0.0";

    public Task OnLoadedAsync(IPluginContext context)
    {
        context.RegisterLifecycleHook(this);
        return Task.CompletedTask;
    }
}
```
