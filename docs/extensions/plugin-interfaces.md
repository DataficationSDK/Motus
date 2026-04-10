# Plugin Interfaces

Motus exposes eight extensibility interfaces that plugins can implement to participate in browser automation, test reporting, accessibility auditing, and performance monitoring. All interfaces live in the `Motus.Abstractions` NuGet package. Plugins register their implementations through `IPluginContext` during initialization.

---

## ISelectorStrategy

`ISelectorStrategy` lets a plugin add custom element-targeting logic beyond the built-in CSS, XPath, and text selectors. When Motus resolves a selector expression, it checks the registered prefix against each strategy's `StrategyName`. Strategies are also consulted during recording to generate candidate selectors for captured elements; the strategy with the highest `Priority` wins when multiple candidates apply.

### Members

| Member | Return Type | Description |
|---|---|---|
| `StrategyName` | `string` | The prefix used in selector strings (e.g. `"data-test"` for `data-test=login-btn`). |
| `Priority` | `int` | Precedence when multiple strategies can resolve the same selector. Higher value wins. |
| `ResolveAsync(string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)` | `Task<IReadOnlyList<IElementHandle>>` | Resolves the selector expression to all matching elements within the given frame. |
| `GenerateSelector(IElementHandle element, CancellationToken ct = default)` | `Task<string?>` | Generates the best selector for the given element, or `null` if this strategy cannot produce one. |

### Lifecycle Notes

`ResolveAsync` is called on every element lookup that uses the registered prefix. It must be safe to call concurrently from parallel test workers. Returning an empty list is the correct response when no elements match; throwing an exception aborts the current action and propagates as a test failure.

`GenerateSelector` is called during recording and codegen. Returning `null` signals that this strategy passes on the element; another strategy or the built-in fallback will be tried.

### Example

```csharp
using Motus.Abstractions;

public sealed class DataTestIdStrategy : ISelectorStrategy
{
    public string StrategyName => "data-test";
    public int Priority => 100;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector,
        IFrame frame,
        bool pierceShadow = true,
        CancellationToken ct = default)
    {
        var jsSelector = $"[data-test=\"{selector}\"]";
        return await frame.QuerySelectorAllAsync(jsSelector);
    }

    public async Task<string?> GenerateSelector(
        IElementHandle element,
        CancellationToken ct = default)
    {
        var value = await element.GetAttributeAsync("data-test");
        return value is not null ? value : null;
    }
}
```

---

## ILifecycleHook

`ILifecycleHook` intercepts navigation, user actions, page open/close events, console output, and uncaught page errors. Multiple hooks can be registered simultaneously; they are invoked in registration order. Common uses include performance measurement, automatic screenshot capture on failure, and structured audit logging.

### Members

| Member | Return Type | Description |
|---|---|---|
| `BeforeNavigationAsync(IPage page, string url)` | `Task` | Called before each page navigation begins. |
| `AfterNavigationAsync(IPage page, IResponse? response)` | `Task` | Called after each navigation completes. `response` is `null` when navigation produced no HTTP response. |
| `BeforeActionAsync(IPage page, string action)` | `Task` | Called before each user action (`"click"`, `"fill"`, `"type"`, etc.). |
| `AfterActionAsync(IPage page, string action, ActionResult result)` | `Task` | Called after each user action. `result.Error` is `null` on success. |
| `OnPageCreatedAsync(IPage page)` | `Task` | Called when a new page is created (including popup pages). |
| `OnPageClosedAsync(IPage page)` | `Task` | Called when a page is closed. |
| `OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message)` | `Task` | Called when a console message is emitted from the page. |
| `OnPageErrorAsync(IPage page, PageErrorEventArgs error)` | `Task` | Called when an uncaught JavaScript error occurs on the page. |

### Lifecycle Notes

All methods are called from the engine's internal dispatch loop. Implementations must not block the calling thread for extended periods; use `async`/`await` for any I/O. Exceptions thrown from hook methods are caught and logged by the engine but do not fail the running test. Thread safety is the implementor's responsibility when state is shared between hook invocations.

`ActionResult` carries the original action name and an optional `Exception`. Hooks can inspect `result.Error` to distinguish success from failure without duplicating try/catch logic.

### Example

```csharp
using Motus.Abstractions;

public sealed class TimingHook : ILifecycleHook
{
    private readonly IMotusLogger _logger;
    private long _navStart;

    public TimingHook(IMotusLogger logger) => _logger = logger;

    public Task BeforeNavigationAsync(IPage page, string url)
    {
        _navStart = Environment.TickCount64;
        return Task.CompletedTask;
    }

    public Task AfterNavigationAsync(IPage page, IResponse? response)
    {
        var elapsed = Environment.TickCount64 - _navStart;
        _logger.Log($"Navigation completed in {elapsed} ms (status {response?.Status})");
        return Task.CompletedTask;
    }

    public Task BeforeActionAsync(IPage page, string action) => Task.CompletedTask;
    public Task AfterActionAsync(IPage page, string action, ActionResult result) => Task.CompletedTask;
    public Task OnPageCreatedAsync(IPage page) => Task.CompletedTask;
    public Task OnPageClosedAsync(IPage page) => Task.CompletedTask;
    public Task OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message) => Task.CompletedTask;
    public Task OnPageErrorAsync(IPage page, PageErrorEventArgs error) => Task.CompletedTask;
}
```

---

## IWaitCondition

`IWaitCondition` defines a named condition that can be used with `WaitForAsync`. The engine polls `EvaluateAsync` at the configured interval until it returns `true` or the timeout elapses. Custom conditions let tests wait for application-specific states (animation completion, data loading, WebSocket readiness) without embedding raw JavaScript into test code.

### Members

| Member | Return Type | Description |
|---|---|---|
| `ConditionName` | `string` | The name used in wait expressions (e.g. `"animation-complete"`). |
| `EvaluateAsync(IPage page, WaitConditionOptions? options = null)` | `Task<bool>` | Returns `true` when the condition is satisfied. Called repeatedly until `true` or timeout. |

### Lifecycle Notes

`EvaluateAsync` is called on a polling loop. Each call receives the same `options` instance for the duration of a single `WaitForAsync` call. Returning `false` simply triggers the next poll; throwing an exception aborts the wait and surfaces as a timeout error. `WaitConditionOptions` exposes `Timeout` (ms) and `PollingInterval` (ms); passing `null` lets the engine apply its defaults.

### Example

```csharp
using Motus.Abstractions;

public sealed class NetworkIdleCondition : IWaitCondition
{
    public string ConditionName => "network-idle";

    public async Task<bool> EvaluateAsync(IPage page, WaitConditionOptions? options = null)
    {
        // Returns true when no active network requests remain.
        var pending = await page.EvaluateAsync<int>("() => window.__pendingRequests ?? 0");
        return pending == 0;
    }
}
```

---

## IReporter

`IReporter` receives test run lifecycle events and is the primary integration point for custom output formats, external dashboards, and CI notification systems. Multiple reporters can be registered; all receive each event in registration order. An `IReporter` implementation may also implement `IAccessibilityReporter` to receive violation events alongside standard lifecycle events.

### Members

| Member | Return Type | Description |
|---|---|---|
| `OnTestRunStartAsync(TestSuiteInfo suite)` | `Task` | Called once before any tests execute. Receives full suite metadata. |
| `OnTestStartAsync(TestInfo test)` | `Task` | Called before each individual test begins. |
| `OnTestEndAsync(TestInfo test, TestResult result)` | `Task` | Called after each test completes with pass/fail status, duration, errors, and attachments. |
| `OnTestRunEndAsync(TestRunSummary summary)` | `Task` | Called once after all tests have executed. Receives aggregate pass/fail/skip counts and total duration. |

### Supporting Types

`TestSuiteInfo(string SuiteName, int TestCount, IReadOnlyList<string>? Tags)` carries suite-level metadata at run start.

`TestInfo(string TestName, string SuiteName, IReadOnlyList<string>? Tags)` identifies the current test.

`TestResult(string TestName, bool Passed, double DurationMs, string? ErrorMessage, string? StackTrace, IReadOnlyList<string>? Attachments)` carries the outcome. `Attachments` contains file paths to screenshots, traces, or other artifacts captured during the test.

`TestRunSummary(string SuiteName, int Passed, int Failed, int Skipped, double TotalDurationMs)` provides aggregate counts for the completed run.

### Lifecycle Notes

Methods are invoked sequentially per event type; the engine does not parallelize reporter dispatch. Exceptions thrown from reporter methods are caught and logged but do not affect test execution. `OnTestRunEndAsync` is guaranteed to be called even if tests are aborted mid-run.

### Example

```csharp
using Motus.Abstractions;

public sealed class ConsoleReporter : IReporter
{
    public Task OnTestRunStartAsync(TestSuiteInfo suite)
    {
        Console.WriteLine($"Starting {suite.SuiteName} ({suite.TestCount} tests)");
        return Task.CompletedTask;
    }

    public Task OnTestStartAsync(TestInfo test)
    {
        Console.WriteLine($"  Running: {test.TestName}");
        return Task.CompletedTask;
    }

    public Task OnTestEndAsync(TestInfo test, TestResult result)
    {
        var status = result.Passed ? "PASS" : "FAIL";
        Console.WriteLine($"  [{status}] {test.TestName} ({result.DurationMs:F0} ms)");
        if (!result.Passed && result.ErrorMessage is not null)
            Console.WriteLine($"         {result.ErrorMessage}");
        return Task.CompletedTask;
    }

    public Task OnTestRunEndAsync(TestRunSummary summary)
    {
        Console.WriteLine($"Done. Passed: {summary.Passed}, Failed: {summary.Failed}, Skipped: {summary.Skipped}");
        return Task.CompletedTask;
    }
}
```

---

## IAccessibilityReporter

`IAccessibilityReporter` is an opt-in companion to `IReporter` for reporters that want to receive accessibility violation events. Motus checks `reporter is IAccessibilityReporter` at runtime on each registered reporter; only those that implement both interfaces receive violation callbacks. This design avoids NativeAOT trimming issues that arise from default interface method implementations.

### Members

| Member | Return Type | Description |
|---|---|---|
| `OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)` | `Task` | Called when an accessibility violation is detected during test execution. |

### Supporting Type: AccessibilityViolation

```
AccessibilityViolation(
    string RuleId,
    AccessibilityViolationSeverity Severity,
    string Message,
    string? NodeRole,
    string? NodeName,
    long? BackendDOMNodeId,
    string? Selector)
```

`Severity` is one of `Error`, `Warning`, or `Info`. `Selector` is a best-effort CSS selector for the violating element and may be `null` when the element cannot be resolved to a unique path.

### Lifecycle Notes

`OnAccessibilityViolationAsync` may be called multiple times per test, once per violation found during that test's execution. It is always called before `OnTestEndAsync` for the same test. Exceptions are caught and logged; they do not suppress remaining violation events.

### Example

```csharp
using Motus.Abstractions;

public sealed class A11yReporter : IReporter, IAccessibilityReporter
{
    private readonly List<AccessibilityViolation> _violations = [];

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;
    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;
    public Task OnTestRunEndAsync(TestRunSummary summary) => Task.CompletedTask;

    public Task OnTestEndAsync(TestInfo test, TestResult result) => Task.CompletedTask;

    public Task OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)
    {
        _violations.Add(violation);
        Console.WriteLine(
            $"[{violation.Severity}] {violation.RuleId}: {violation.Message} " +
            $"(selector: {violation.Selector ?? "unknown"})");
        return Task.CompletedTask;
    }
}
```

---

## IPerformanceReporter

`IPerformanceReporter` is an opt-in companion to `IReporter` for reporters that want to receive performance metrics collected during test execution. Motus checks `reporter is IPerformanceReporter` at runtime on each registered reporter; only those that implement both interfaces receive performance callbacks. This follows the same NativeAOT-safe pattern as `IAccessibilityReporter`.

### Members

| Member | Return Type | Description |
|---|---|---|
| `OnPerformanceMetricsCollectedAsync(PerformanceMetrics metrics, PerformanceBudgetResult? budgetResult, TestInfo test)` | `Task` | Called when performance metrics have been collected for a test. |

### Supporting Types

`PerformanceMetrics` carries the collected values: `Lcp`, `Fcp`, `Ttfb`, `Cls`, `Inp` (all `double?`), `JsHeapSize` (`long?`), `DomNodeCount` (`int?`), `LayoutShifts` (`IReadOnlyList<LayoutShiftEntry>`), `CollectedAtUtc` (`DateTime`), and `DiagnosticMessage` (`string?`).

`PerformanceBudgetResult` carries the evaluation: `Entries` (`IReadOnlyList<PerformanceBudgetEntry>`) and `Passed` (`bool`). Each `PerformanceBudgetEntry` has `MetricName`, `Threshold`, `ActualValue`, `Passed`, and `Delta`.

`budgetResult` is `null` when no budget is configured.

### Lifecycle Notes

`OnPerformanceMetricsCollectedAsync` is called once per test when metrics have been collected (typically after a navigation). It is called before `OnTestEndAsync` for the same test. Exceptions are caught and logged; they do not suppress remaining reporter events.

### Example

```csharp
using Motus.Abstractions;

public sealed class PerfReporter : IReporter, IPerformanceReporter
{
    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;
    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;
    public Task OnTestEndAsync(TestInfo test, TestResult result) => Task.CompletedTask;
    public Task OnTestRunEndAsync(TestRunSummary summary) => Task.CompletedTask;

    public Task OnPerformanceMetricsCollectedAsync(
        PerformanceMetrics metrics,
        PerformanceBudgetResult? budgetResult,
        TestInfo test)
    {
        Console.WriteLine(
            $"[{test.TestName}] LCP={metrics.Lcp:F0}ms " +
            $"Budget: {(budgetResult?.Passed == true ? "PASS" : budgetResult?.Passed == false ? "FAIL" : "none")}");
        return Task.CompletedTask;
    }
}
```

---

## IAccessibilityRule

`IAccessibilityRule` evaluates a single WCAG rule against one node in the accessibility tree. The engine walks every non-ignored node in the tree and calls each registered rule's `Evaluate` method. Rules are synchronous to keep the audit loop efficient; use the `AccessibilityAuditContext` for any cross-node data rather than performing additional async page queries.

### Members

| Member | Return Type | Description |
|---|---|---|
| `RuleId` | `string` | Unique identifier for this rule (e.g. `"a11y-alt-text"`). |
| `Description` | `string` | Human-readable description of what this rule checks. |
| `Evaluate(AccessibilityNode node, AccessibilityAuditContext context)` | `AccessibilityViolation?` | Evaluates the rule against the node. Returns a violation record if the node fails, or `null` if it passes or the rule does not apply. |

### Supporting Type: AccessibilityNode

```
AccessibilityNode(
    string NodeId,
    string? Role,
    string? Name,
    string? Value,
    string? Description,
    IReadOnlyDictionary<string, string?> Properties,
    IReadOnlyList<AccessibilityNode> Children,
    long? BackendDOMNodeId,
    bool Ignored = false)
```

`AccessibilityAuditContext` provides `AllNodes` (depth-first walkable tree), the live `Page`, pre-fetched `ComputedStyles` keyed by `BackendDOMNodeId`, a `DuplicateIds` set for HTML `id` collision checks, and `DocumentLanguage` from the `<html lang>` attribute.

### Lifecycle Notes

`Evaluate` is called once per node per registered rule during each audit pass. It runs synchronously on the audit thread; implementations must not perform blocking I/O. Return `null` for nodes where the rule does not apply rather than returning a low-severity violation. The `RuleId` on the returned `AccessibilityViolation` should match the rule's own `RuleId`.

### Example

```csharp
using Motus.Abstractions;

public sealed class ImageAltTextRule : IAccessibilityRule
{
    public string RuleId => "a11y-alt-text";
    public string Description => "Images must have a non-empty accessible name.";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        if (node.Role is not "img")
            return null;

        if (!string.IsNullOrWhiteSpace(node.Name))
            return null;

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Error,
            Message: "Image element has no accessible name (missing or empty alt attribute).",
            NodeRole: node.Role,
            NodeName: node.Name,
            BackendDOMNodeId: node.BackendDOMNodeId,
            Selector: null);
    }
}
```

---

## IMotusLogger

`IMotusLogger` is the structured logging interface for plugin diagnostics. It is intentionally minimal so that the `Motus.Abstractions` package remains dependency-free. Obtain an instance by calling `IPluginContext.CreateLogger(string categoryName)` during plugin initialization; the engine routes log output to the configured sink (console, file, or external provider).

### Members

| Member | Return Type | Description |
|---|---|---|
| `Log(string message)` | `void` | Logs an informational message. |
| `LogWarning(string message)` | `void` | Logs a warning message. |
| `LogError(string message, Exception? exception = null)` | `void` | Logs an error message, optionally with an associated exception. |

### Lifecycle Notes

All three methods are synchronous and safe to call from any thread. The `categoryName` passed to `CreateLogger` scopes log entries so they can be filtered by plugin name. Do not cache `IMotusLogger` instances across plugin reload boundaries; request a fresh instance each time the plugin is initialized.

### Example

```csharp
using Motus.Abstractions;

public sealed class MyPlugin : IMotusPlugin
{
    private IMotusLogger _logger = null!;

    public void Initialize(IPluginContext context)
    {
        _logger = context.CreateLogger(nameof(MyPlugin));
        _logger.Log("Plugin initialized.");
    }
}
```

---

## IPluginContext

`IPluginContext` is provided to every plugin's `Initialize` method. It is the sole registration point for all extensions and the factory for scoped loggers. Registrations made outside of `Initialize` are not guaranteed to take effect before the first test runs.

### Members

| Member | Return Type | Description |
|---|---|---|
| `RegisterSelectorStrategy(ISelectorStrategy strategy)` | `void` | Registers a custom selector strategy. |
| `RegisterWaitCondition(IWaitCondition condition)` | `void` | Registers a custom wait condition. |
| `RegisterLifecycleHook(ILifecycleHook hook)` | `void` | Registers a lifecycle hook. |
| `RegisterReporter(IReporter reporter)` | `void` | Registers a test reporter. Reporters that also implement `IAccessibilityReporter` or `IPerformanceReporter` automatically receive the corresponding events. |
| `RegisterAccessibilityRule(IAccessibilityRule rule)` | `void` | Registers a custom accessibility rule that is invoked during accessibility audits. |
| `CreateLogger(string categoryName)` | `IMotusLogger` | Creates a logger scoped to the given category name. |

### Example

```csharp
using Motus.Abstractions;

public sealed class MyPlugin : IMotusPlugin
{
    public void Initialize(IPluginContext context)
    {
        var logger = context.CreateLogger(nameof(MyPlugin));

        context.RegisterSelectorStrategy(new DataTestIdStrategy());
        context.RegisterWaitCondition(new NetworkIdleCondition());
        context.RegisterLifecycleHook(new TimingHook(logger));
        context.RegisterReporter(new A11yReporter());
        context.RegisterAccessibilityRule(new ImageAltTextRule());

        logger.Log("All extensions registered.");
    }
}
```
