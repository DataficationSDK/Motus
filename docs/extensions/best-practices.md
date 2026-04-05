# Plugin Best Practices

This guide collects hard-won guidelines for writing production-quality Motus plugins. The rules here are derived directly from how the Motus engine itself is implemented: the same interfaces, the same constraints, and the same failure modes apply equally to built-in and third-party code.

---

## NativeAOT Compatibility

Motus is designed to publish as a NativeAOT binary. Plugins that ship as NuGet packages or as statically linked assemblies must meet the same requirements.

**Avoid reflection.** Do not call `Type.GetType`, `Assembly.LoadFrom`, `Activator.CreateInstance`, or any other reflection API on types that are not known at compile time. NativeAOT cannot generate the necessary metadata for types discovered at runtime.

**Use System.Text.Json source generators for serialization.** If your plugin serializes or deserializes JSON (for example, when writing a reporter that posts results to an HTTP endpoint), use a `JsonSerializerContext` instead of the reflection-based overloads.

```csharp
// Good: source-generated serialization
[JsonSerializable(typeof(SlackPayload))]
internal partial class SlackJsonContext : JsonSerializerContext { }

var json = JsonSerializer.Serialize(payload, SlackJsonContext.Default.SlackPayload);
```

```csharp
// Bad: reflection-based serialization, stripped by NativeAOT trimmer
var json = JsonSerializer.Serialize(payload);
```

**Do not load assemblies dynamically.** Plugins discovered by the Motus source generator are compiled into the assembly at build time. If your plugin needs to conditionally incorporate behavior, use compile-time configuration (`#if` or feature flags) rather than loading satellite assemblies at runtime with `Assembly.LoadFile` or `AssemblyLoadContext`.

---

## Thread Safety

Lifecycle hooks and selector strategies can be invoked concurrently when multiple pages or browser contexts are active within the same test run. The engine makes no guarantees about which thread dispatches a given hook call.

**Prefer immutable state.** The safest plugin is one with no mutable fields at all. Stateless implementations of `ISelectorStrategy`, `IAccessibilityRule`, and `IWaitCondition` require no synchronization and can be shared freely across concurrent callers.

**Use `ConcurrentDictionary` or `Interlocked` when state is required.** If your hook needs to accumulate data (for example, a timing hook recording per-URL latency), store it in a thread-safe collection rather than a `List<T>` or `Dictionary<TKey, TValue>` protected by a manual lock.

```csharp
// Good: concurrent accumulation with no locking required in the hot path
private readonly ConcurrentDictionary<string, long> _timing = new();

public Task BeforeNavigationAsync(IPage page, string url)
{
    _timing[url] = Environment.TickCount64;
    return Task.CompletedTask;
}
```

```csharp
// Bad: List<T> is not thread-safe; concurrent writes cause data corruption
private readonly List<(string Url, long Start)> _timing = [];

public Task BeforeNavigationAsync(IPage page, string url)
{
    _timing.Add((url, Environment.TickCount64)); // race condition
    return Task.CompletedTask;
}
```

**Reporters are serialized per event, not across events.** `ReporterCollection` takes a snapshot of the registered reporter list under a lock before dispatching each event, then iterates the snapshot sequentially. Individual reporter methods are therefore called one at a time for a given event, but `OnTestStartAsync` for one test can overlap with `OnTestEndAsync` for another when tests run in parallel. If your reporter maintains state across events, protect it with appropriate synchronization.

---

## Error Handling

**Never throw from lifecycle hook methods.** The engine catches exceptions thrown by user plugins and logs them, but the calling action is not retried. A throw from `BeforeNavigationAsync` does not block the navigation; the exception is silently discarded after logging. Relying on a throw to signal failure is therefore ineffective and may mask the real error. Catch problems inside your hook and log them explicitly with `IMotusLogger`.

```csharp
// Good: catch and log within the hook body
public async Task BeforeNavigationAsync(IPage page, string url)
{
    try
    {
        await _auditService.RecordAsync(url);
    }
    catch (Exception ex)
    {
        _logger.LogError("Failed to record navigation audit", ex);
    }
}
```

```csharp
// Bad: exception is swallowed by the engine with no visible effect on test flow
public async Task BeforeNavigationAsync(IPage page, string url)
{
    await _auditService.RecordAsync(url); // throws; test still continues silently
}
```

**The same pattern applies to reporters.** `ReporterCollection` wraps every dispatch call in a `try { } catch { }` block (see `FireOnTestEndAsync` and all sibling methods). Exceptions thrown by reporters are silently discarded to ensure that one broken reporter cannot prevent other reporters from receiving events. Log errors inside your reporter methods rather than surfacing them as exceptions.

**Exceptions during `OnLoadedAsync` cause the plugin to be skipped.** Built-in plugins (the selector engine, accessibility rules) are loaded before user plugins and are not subject to exception swallowing; they propagate immediately. User plugins that throw from `OnLoadedAsync` are silently skipped and never added to the loaded list. During development, add diagnostic logging at the start of `OnLoadedAsync` to confirm the plugin reached initialization.

**Exceptions during `OnUnloadedAsync` are swallowed.** The engine catches and discards errors from `OnUnloadedAsync` so that a failing plugin cannot block cleanup of the remaining plugins. Do not rely on `OnUnloadedAsync` to propagate errors; use logging instead.

---

## Dogfooding Principle

Every built-in Motus feature is implemented through the same plugin interfaces that are available to third-party plugins. The engine ships no privileged internal paths for first-party code.

**`AccessibilityRulesPlugin`** (`motus.accessibility-rules`) registers all WCAG 2.1 Level A and AA rules by calling `context.RegisterAccessibilityRule(...)` for each rule in `OnLoadedAsync`. It does nothing special that third-party accessibility plugins cannot do. The full list of registered rules (`AltTextAccessibilityRule`, `ColorContrastRule`, `DuplicateIdRule`, and others) is visible in the source and can be used as a reference for writing your own `IAccessibilityRule` implementations.

**`RoleSelectorStrategy`** (`StrategyName = "role"`, `Priority = 30`) is a built-in `ISelectorStrategy`. It uses span slicing instead of regular expressions, defers work to the CDP Accessibility domain, and uses `volatile bool` for the single per-instance flag it must track. These choices reflect the constraints documented in this guide: no reflection, minimal mutable state, and no synchronization primitives beyond `volatile` for a flag that transitions in one direction only.

When you are unsure how to approach a particular pattern, read the built-in implementations first. They represent the intended idiomatic usage of each interface.

---

## Plugin Lifecycle

The engine loads plugins in two phases and unloads them in reverse order.

**Loading order.** Built-in plugins (`BuiltinSelectorsPlugin`, `AccessibilityRulesPlugin`, `AccessibilityAuditHook`) are loaded first. Manual registrations from `LaunchOptions.Plugins` are merged next, followed by auto-discovered plugins from the source generator. Within each group, plugins are loaded in the order they appear. If two plugins share the same `PluginId`, the first one wins; duplicates are silently skipped.

**`OnLoadedAsync` is the registration window.** All `IPluginContext` registrations must happen inside `OnLoadedAsync`. The context is not guaranteed to be valid after that call returns. Capture the `IMotusLogger` instance during this call and store it as a field for later use.

```csharp
public Task OnLoadedAsync(IPluginContext context)
{
    _logger = context.CreateLogger(nameof(MyPlugin));
    context.RegisterLifecycleHook(new MyHook(_logger));
    return Task.CompletedTask;
}
```

**`OnUnloadedAsync` is the cleanup window.** Release file handles, cancel background tasks, flush pending writes, and null out references to managed resources here. The engine unloads plugins in reverse registration order so that a plugin which depends on another plugin is always unloaded before its dependency.

**Do not cache the `IPluginContext` reference.** The context object is scoped to the load call. Storing it and calling registration methods after `OnLoadedAsync` returns produces undefined behavior.

---

## Stateless vs. Stateful Plugins

**Prefer stateless implementations.** `IAccessibilityRule.Evaluate` and `ISelectorStrategy.ResolveAsync` are called repeatedly with no expectation of accumulated state between calls. Implementing them as stateless classes (no instance fields) eliminates thread-safety concerns entirely and makes the implementations trivially testable.

```csharp
// Good: stateless rule, safe for concurrent calls, no synchronization needed
public sealed class NoAutoplayRule : IAccessibilityRule
{
    public string RuleId => "a11y-no-autoplay";
    public string Description => "Media must not autoplay with audio.";

    public AccessibilityViolation? Evaluate(AccessibilityNode node, AccessibilityAuditContext context)
    {
        if (node.Role is not ("video" or "audio"))
            return null;

        var autoplay = node.Properties.GetValueOrDefault("autoplay");
        var muted = node.Properties.GetValueOrDefault("muted");
        if (autoplay is null || muted is "true")
            return null;

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Error,
            Message: "Media element autoplays with audio enabled.",
            NodeRole: node.Role,
            NodeName: node.Name,
            BackendDOMNodeId: node.BackendDOMNodeId,
            Selector: null);
    }
}
```

**Use `ConcurrentDictionary` when state is unavoidable.** If your `ISelectorStrategy` needs to cache resolved nodes across calls (for example, to avoid redundant CDP round-trips), use a `ConcurrentDictionary<string, IElementHandle>` or similar thread-safe type. Clear the cache in `OnUnloadedAsync` if the plugin is reloadable.

**Reporters are the expected home for stateful accumulation.** Reporters are naturally stateful because they must track per-test results across `OnTestStartAsync`, `OnTestEndAsync`, and `OnTestRunEndAsync`. Accumulate in a `ConcurrentDictionary` or flush to an external store after each event rather than buffering indefinitely in a `List<T>`.

---

## Logging

**Use `IPluginContext.CreateLogger`, not `Console.WriteLine`.** Motus routes `IMotusLogger` output through the configured logging pipeline, which may include structured sinks, log-level filtering, and test run correlation. Console output bypasses all of this and is difficult to distinguish from the application under test's own output.

```csharp
// Good: routed through the Motus logging pipeline
private IMotusLogger _logger = null!;

public Task OnLoadedAsync(IPluginContext context)
{
    _logger = context.CreateLogger(nameof(MyPlugin));
    _logger.Log("Plugin loaded.");
    return Task.CompletedTask;
}
```

```csharp
// Bad: unfiltered, unstructured, not correlated with test execution
public Task OnLoadedAsync(IPluginContext context)
{
    Console.WriteLine("Plugin loaded."); // bypasses logging pipeline
    return Task.CompletedTask;
}
```

**Use the appropriate severity level.** Call `_logger.Log` for informational events (navigation recorded, rule evaluated), `_logger.LogWarning` for degraded-but-functional conditions (a selector returned no results when results were expected), and `_logger.LogError` for actual failures including the exception instance where available.

**Scope the category name to the plugin.** Pass `nameof(YourPluginClass)` or a stable dotted name to `CreateLogger`. This allows log consumers to filter output to a specific plugin without modifying the plugin's code.

---

## Testing Plugins

**Unit test rules and strategies independently.** `IAccessibilityRule.Evaluate` and `ISelectorStrategy.ParseRoleSelector`-style parsing logic are pure functions that take typed inputs and return typed outputs. They require no browser, no engine, and no test harness to test. Construct the input types directly and assert on the return value.

```csharp
[Fact]
public void NoAutoplayRule_ReturnNull_WhenNodeIsNotMedia()
{
    var rule = new NoAutoplayRule();
    var node = new AccessibilityNode(
        NodeId: "1", Role: "button", Name: "Submit",
        Value: null, Description: null,
        Properties: ImmutableDictionary<string, string?>.Empty,
        Children: [], BackendDOMNodeId: null);

    var result = rule.Evaluate(node, context: null!);

    Assert.Null(result);
}
```

**Integration test with a real browser for hooks and reporters.** Lifecycle hooks receive `IPage` and `IResponse` instances that are only meaningful in the context of a live CDP session. Use `Motus.LaunchAsync` with `LaunchOptions.Plugins` to load the plugin under test, then drive the browser through the scenario and assert on any side effects (log messages captured, files written, external calls made).

**Mock `IPluginContext` for load tests.** Verify that `OnLoadedAsync` registers the expected number of strategies, hooks, or rules by providing a test double for `IPluginContext` that records registration calls.

```csharp
[Fact]
public async Task Plugin_RegistersExpectedComponents()
{
    var context = new FakePluginContext();
    var plugin = new MyPlugin();

    await plugin.OnLoadedAsync(context);

    Assert.Single(context.RegisteredStrategies);
    Assert.Single(context.RegisteredHooks);
}
```

**Test unload does not throw.** Even if your `OnUnloadedAsync` is a one-liner returning `Task.CompletedTask`, verify it in a unit test. This guards against future changes that accidentally introduce a throw path.

---

## See Also

- [Getting Started with Plugins](getting-started.md) - project setup, `[MotusPlugin]` discovery, and manual registration
- [Plugin Interfaces Reference](plugin-interfaces.md) - detailed documentation for `ISelectorStrategy`, `ILifecycleHook`, `IWaitCondition`, `IReporter`, `IAccessibilityReporter`, `IAccessibilityRule`, `IMotusLogger`, and `IPluginContext`
