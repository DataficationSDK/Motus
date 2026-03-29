# Motus.Codegen

Roslyn incremental source generator for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework.

## Overview

This generator produces two categories of code at compile time. First, it reads the bundled `browser_protocol.json` and `js_protocol.json` files and emits strongly-typed C# bindings for every CDP domain, enabling NativeAOT compatibility without runtime reflection. Second, it scans for types annotated with `[MotusPlugin]` and emits a module initializer that registers them with `PluginDiscovery`, so plugins are discovered at startup with no assembly scanning.

### Generated Output

- **CDP domain bindings** emitted as `Motus.Protocol.<DomainName>Domain.g.cs` for each protocol domain
- **Plugin registry** emitted as `MotusPluginRegistry.g.cs` with a module initializer

### Plugin Diagnostics

| ID | Severity | Description |
|----|----------|-------------|
| MOTUS001 | Error | `[MotusPlugin]` on an abstract class |
| MOTUS002 | Error | `[MotusPlugin]` on a generic class |
| MOTUS003 | Error | `[MotusPlugin]` on a class that does not implement `IPlugin` |
| MOTUS004 | Error | `[MotusPlugin]` on a class without a public parameterless constructor |

## Installation

```shell
dotnet add package Motus.Codegen
```

This package is automatically included when you reference [Motus](https://www.nuget.org/packages/Motus). Direct installation is only needed for advanced scenarios.
