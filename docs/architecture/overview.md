# Architecture Overview

Motus is a .NET browser automation framework that communicates directly with Chromium and Firefox over WebSocket using CDP and WebDriver BiDi, with no Node.js dependency. Every built-in feature is registered through the same `IPluginContext` available to third-party plugins, keeping the architecture honest by design.

## Project Structure

| Project | Role |
|---------|------|
| `Motus.Abstractions` | Public interfaces and types; zero dependencies; the contract between the engine and all consumers |
| `Motus` | Core engine: transport, browser management, page controller, locators, selectors, and assertions |
| `Motus.Codegen` | Roslyn source generator for CDP/BiDi protocol types and compile-time plugin discovery |
| `Motus.Analyzers` | Roslyn diagnostic analyzers and code fixes for common automation mistakes |
| `Motus.Recorder` | Action capture, selector inference, and Page Object Model generation |
| `Motus.Runner` | Blazor visual test runner with live screencast and action timeline |
| `Motus.Cli` | `motus` CLI tool (`run`, `record`, `codegen`, `screenshot`, `pdf`, `trace`, `install`) |
| `Motus.Testing` | Shared base types for test framework integrations |
| `Motus.Testing.MSTest` | MSTest integration (`MotusTestBase`, lifecycle management) |
| `Motus.Testing.xUnit` | xUnit integration |
| `Motus.Testing.NUnit` | NUnit integration |
| `Motus.Samples` | Runnable usage examples |
| `Motus.Tests` | Unit and integration tests for the core engine |
| `Motus.Abstractions.Tests` | Tests for the abstractions layer |
| `Motus.Codegen.Tests` | Tests for the source generator |
| `Motus.Analyzers.Tests` | Tests for Roslyn diagnostics |
| `Motus.Recorder.Tests` | Tests for the recorder and POM generator |
| `Motus.Cli.Tests` | Tests for the CLI tool |

## Layered Architecture

The stack has four layers. Test code at the top calls high-level abstractions; those translate into protocol messages that travel over a WebSocket to the browser process at the bottom.

```
┌────────────────────────────────────────────────────────┐
│                      Test Code                         │
│   (MSTest / xUnit / NUnit via Motus.Testing.*)         │
├────────────────────────────────────────────────────────┤
│             Assertions  /  Locators                    │
│   Expect.That(...), page.Locator("css"), auto-wait     │
├────────────────────────────────────────────────────────┤
│          Page  /  BrowserContext  /  IBrowser          │
│   navigation, network interception, lifecycle hooks    │
├────────────────────────────────────────────────────────┤
│               Transport Layer                          │
│   CdpTransport (Chromium)  |  BiDiTransport (Firefox)  │
│   CdpSocket — raw WebSocket, no Node.js, no driver     │
├────────────────────────────────────────────────────────┤
│               Browser Process                          │
│   Chrome / Edge / Firefox (launched or connected)      │
└────────────────────────────────────────────────────────┘
```

## Data Flow

The following describes how a single test action, for example `page.Locator("button").ClickAsync()`, moves through the layers.

1. **Locator resolution** - The `Locator` type holds the selector string. When an action is invoked, the registered `ISelectorStrategy` implementations are consulted to resolve the element in the DOM.
2. **Auto-wait** - Before dispatching the action, the engine polls actionability conditions (visible, enabled, stable, receives events) using `IWaitCondition`. Registered `ILifecycleHook` implementations are notified at the start of the action.
3. **Command dispatch** - The resolved command (for example a CDP `Input.dispatchMouseEvent` sequence) is serialized to JSON using `System.Text.Json` source generators and handed to `CdpTransport` or `BiDiTransport`.
4. **Transport** - `CdpSocket` sends the message over the open WebSocket connection and awaits the response frame. `SlowMo` delay, if configured, is applied here before sending.
5. **Protocol response** - The browser's response is deserialized by the source-generated `JsonSerializerContext` and returned up the call stack.
6. **Lifecycle notification** - `ILifecycleHook` implementations are notified that the action completed (or failed). Reporters receive the event if they implement the relevant hook surface.

## Protocol Layer

| Browser | Protocol | Connection |
|---------|----------|------------|
| Chromium (Chrome, Edge) | Chrome DevTools Protocol (CDP) | `CdpTransport` over `CdpSocket` (WebSocket) |
| Firefox | WebDriver BiDi | `BiDiTransport` over `CdpSocket` (WebSocket) |

`MotusLauncher.LaunchAsync` allocates a free TCP port, starts the browser process with the appropriate remote debugging flags, and then polls (Chromium) or reads stderr (Firefox) to discover the WebSocket endpoint. `MotusLauncher.ConnectAsync` skips process management entirely and attaches to an existing browser via a supplied WebSocket URL using `CdpTransport`.

There is no Node.js sidecar, no WebDriver HTTP server, and no intermediate process boundary. The `CdpSocket` class holds the raw `ClientWebSocket` connection directly.

## Plugin System

Five primary interfaces define every extension point. Two additional interfaces provide opt-in capabilities for specialized scenarios. All are registered through `IPluginContext` inside a plugin's `OnLoadedAsync` method.

| Interface | Purpose |
|-----------|---------|
| `ISelectorStrategy` | Resolve elements and generate selectors for a custom prefix (e.g. `data-testid=`) |
| `ILifecycleHook` | Intercept navigation, actions, page create/close, console messages, and errors |
| `IWaitCondition` | Define named wait conditions usable with `WaitForAsync` |
| `IReporter` | Receive test run events for custom output (multiple reporters run simultaneously) |
| `IAccessibilityRule` | Define custom WCAG rules evaluated against the browser accessibility tree |
| `IAccessibilityReporter` | Opt-in extension for reporters that need per-violation accessibility events |
| `IMotusLogger` | Structured logging surface for plugin diagnostics |

Plugins implement `IPlugin` and expose a `PluginId`, `Name`, and `Version`. The engine calls `OnLoadedAsync(IPluginContext)` at startup and `OnUnloadedAsync()` at shutdown.

All five built-in selector strategies, the four built-in reporters, the visual runner timeline hook, and the nine built-in WCAG accessibility rules are registered through `IPluginContext`. They have no special access to engine internals.

### Plugin Discovery

Plugins are discovered at two points:

1. **Compile-time auto-discovery** - Annotating a class with `[MotusPlugin]` causes `Motus.Codegen` to emit a `[ModuleInitializer]` that registers the plugin at process startup. No reflection is used.
2. **Manual registration** - Plugin instances passed via `LaunchOptions.Plugins` are registered before `OnLoadedAsync` is called on any plugin.

## NativeAOT Design

`Motus.csproj` sets `<PublishAot>true</PublishAot>` and `<IsAotCompatible>true</IsAotCompatible>`. The engine avoids reflection throughout:

- **Protocol types** - `Motus.Codegen` reads `browser_protocol.json` and `js_protocol.json` at build time and emits strongly-typed C# command and event classes together with a `System.Text.Json` `JsonSerializerContext` covering every generated type. Runtime serialization calls source-generated converters directly.
- **Plugin discovery** - The `[MotusPlugin]` source generator emits `[ModuleInitializer]` registration calls, replacing any assembly-scanning approach.
- **No dynamic code** - There is no `Activator.CreateInstance`, no `Type.GetMethod`, and no `Expression.Compile` in the core engine or abstractions layer.

This means `dotnet publish -r <rid> -p:PublishAot=true` produces a fully trimmed, ahead-of-time compiled binary with no trimming warnings from the Motus packages themselves.

## See Also

- [Plugin System](plugin-system.md) - full interface reference and registration lifecycle
- [Transport Layer](transport.md) - CDP and BiDi protocol details, session management, and SlowMo
- [Selector Strategies](selector-strategies.md) - built-in strategies and how to implement `ISelectorStrategy`
- [NativeAOT](nativeaot.md) - source generator details and AOT publishing guide
