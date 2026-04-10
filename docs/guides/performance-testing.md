# Performance Testing

Motus collects Core Web Vitals and supplementary performance metrics from the browser during test execution, directly from the CDP `Performance` domain and the `PerformanceObserver` API. Metrics are collected automatically after each navigation when the performance hook is enabled. Budget thresholds can be configured per-test, per-class, or globally so that performance regressions fail the build.

---

## Metrics

The `PerformanceMetrics` record carries these fields after each collection:

| Metric | Type | Unit | Source |
|---|---|---|---|
| `Lcp` | `double?` | ms | Largest Contentful Paint via `PerformanceObserver` |
| `Fcp` | `double?` | ms | First Contentful Paint via `PerformanceObserver` (CDP fallback) |
| `Ttfb` | `double?` | ms | Time to First Byte via Navigation Timing API |
| `Cls` | `double?` | unitless | Cumulative Layout Shift via `PerformanceObserver` |
| `Inp` | `double?` | ms | Interaction to Next Paint via `PerformanceObserver` |
| `JsHeapSize` | `long?` | bytes | JavaScript heap used size via CDP `Performance.getMetrics` |
| `DomNodeCount` | `int?` | count | DOM node count via CDP `Performance.getMetrics` |
| `LayoutShifts` | list | -- | Individual layout shift entries with score and source elements |

Metrics that have not been observed (e.g. `Inp` when no interactions occurred) remain `null`. For BiDi transports where CDP performance domains are unavailable, `JsHeapSize` and `DomNodeCount` are `null`.

---

## Page-Level Assertions

### Budget Assertion

`ToMeetPerformanceBudgetAsync` evaluates all configured thresholds against the collected metrics. The budget is resolved from (in order): `[PerformanceBudget]` attribute on the test method, then class, then `motus.config.json`. The assertion fails with a table showing each metric, its threshold, actual value, and how far it exceeded the budget.

```csharp
// Assert all metrics are within the active budget
await Expect.That(page).ToMeetPerformanceBudgetAsync();
```

### Individual Metric Assertions

Each metric has a dedicated assertion method with auto-retry semantics. The assertion polls the collected metrics until the condition is met or the timeout elapses.

```csharp
await Expect.That(page).ToHaveLcpBelowAsync(2500);
await Expect.That(page).ToHaveFcpBelowAsync(1800);
await Expect.That(page).ToHaveTtfbBelowAsync(600);
await Expect.That(page).ToHaveClsBelowAsync(0.1);
await Expect.That(page).ToHaveInpBelowAsync(200);
```

### Negation

All performance assertions support `.Not`:

```csharp
// Assert the page does NOT meet the budget (useful for negative tests)
await Expect.That(page).Not.ToMeetPerformanceBudgetAsync();

// Assert LCP is NOT below 100ms (i.e. it's at least 100ms)
await Expect.That(page).Not.ToHaveLcpBelowAsync(100);
```

### Assertion Reference

| Method | Parameters | Description |
|---|---|---|
| `ToMeetPerformanceBudgetAsync` | `AssertionOptions?` | Evaluates the active budget (attribute or config) against all collected metrics. |
| `ToHaveLcpBelowAsync` | `double thresholdMs, AssertionOptions?` | Asserts Largest Contentful Paint is below the threshold. |
| `ToHaveFcpBelowAsync` | `double thresholdMs, AssertionOptions?` | Asserts First Contentful Paint is below the threshold. |
| `ToHaveTtfbBelowAsync` | `double thresholdMs, AssertionOptions?` | Asserts Time to First Byte is below the threshold. |
| `ToHaveClsBelowAsync` | `double threshold, AssertionOptions?` | Asserts Cumulative Layout Shift is below the threshold. |
| `ToHaveInpBelowAsync` | `double thresholdMs, AssertionOptions?` | Asserts Interaction to Next Paint is below the threshold. |

---

## `[PerformanceBudget]` Attribute

Apply `[PerformanceBudget]` to a test method or class to declare metric thresholds. Only specified properties are enforced; omitted properties default to `-1` (not enforced).

```csharp
// Class-level budget applies to all tests in the class
[TestClass]
[PerformanceBudget(Lcp = 2500, Fcp = 1800, Cls = 0.1)]
public class DashboardTests : MotusTestBase
{
    [TestMethod]
    public async Task DashboardLoadsWithinBudget()
    {
        await Page.GotoAsync("https://app.example.com/dashboard");
        await Expect.That(Page).ToMeetPerformanceBudgetAsync();
    }

    // Method-level attribute overrides the class-level budget
    [TestMethod]
    [PerformanceBudget(Lcp = 1500)]
    public async Task CriticalPageHasTighterBudget()
    {
        await Page.GotoAsync("https://app.example.com/checkout");
        await Expect.That(Page).ToMeetPerformanceBudgetAsync();
    }
}
```

### Attribute Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Lcp` | `double` | `-1` | Maximum LCP in milliseconds. |
| `Fcp` | `double` | `-1` | Maximum FCP in milliseconds. |
| `Ttfb` | `double` | `-1` | Maximum TTFB in milliseconds. |
| `Cls` | `double` | `-1` | Maximum CLS score (unitless). |
| `Inp` | `double` | `-1` | Maximum INP in milliseconds. |
| `JsHeapSize` | `long` | `-1` | Maximum JS heap size in bytes. |
| `DomNodeCount` | `int` | `-1` | Maximum DOM node count. |

The sentinel value `-1` means "not enforced." Zero is a valid threshold.

### Budget Resolution Order

When `ToMeetPerformanceBudgetAsync` is called, the budget is resolved in this order:

1. Page-level budget set via `PerformanceBudgetContext.SetBudget(page, budget)` (used internally by test adapters)
2. `[PerformanceBudget]` attribute on the test method
3. `[PerformanceBudget]` attribute on the test class
4. `performance` section in `motus.config.json` (or `MOTUS_PERFORMANCE_*` environment variables)
5. If none of the above provides a budget, the assertion throws `InvalidOperationException`

The built-in test adapters (MSTest, NUnit, xUnit) resolve the `[PerformanceBudget]` attribute during test setup and call `SetBudget` on the page automatically. You only need `PerformanceBudgetContext.SetBudget` directly when assigning a budget programmatically outside a test adapter.

---

## Performance Metrics Collector

The built-in `PerformanceMetricsCollector` is an `ILifecycleHook` that handles all metric collection. It is disabled by default.

### When It Runs

| Event | Behavior |
|---|---|
| Page created | Enables CDP `Performance.enable` and injects the `PerformanceObserver` script. |
| After navigation | Collects a full metrics snapshot (when `CollectAfterNavigation` is `true`). |
| Page closed | Performs a final metrics collection before teardown. |

### Enabling in Code

```csharp
var browser = await Motus.LaunchAsync(new LaunchOptions
{
    Performance = new PerformanceOptions
    {
        Enable = true,
        CollectAfterNavigation = true,
    }
});
```

### PerformanceOptions Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Enable` | `bool` | `false` | Master switch for the performance metrics collector. |
| `CollectAfterNavigation` | `bool` | `true` | Collect metrics after each page navigation. |

---

## CLI Integration

The `--perf-budget` flag enables performance budget enforcement from the command line. When set, it enables the performance collector and fails tests that exceed their configured budgets.

```bash
# Enable performance budget enforcement
motus run MyTests.dll --perf-budget

# Combine with accessibility enforcement
motus run MyTests.dll --perf-budget --a11y enforce

# Output metrics in the HTML report
motus run MyTests.dll --perf-budget --reporter html:./reports/result.html
```

When `--perf-budget` is active, a test that passes functionally but exceeds any budget threshold is marked as failed with a message indicating which metrics were over budget.

---

## motus.config.json

The `performance` section configures both the collector and budget thresholds. Setting `enable` to `true` activates the collector. Adding metric thresholds activates budget enforcement.

```json
{
  "performance": {
    "enable": true,
    "collectAfterNavigation": true,
    "lcp": 2500,
    "fcp": 1800,
    "cls": 0.1,
    "inp": 200,
    "jsHeapSize": 50000000,
    "domNodeCount": 1500
  }
}
```

### Property Reference

| Property | Type | Default | Description |
|---|---|---|---|
| `enable` | `bool` | `false` | Enable the performance metrics collector. |
| `collectAfterNavigation` | `bool` | `true` | Collect metrics after each navigation. |
| `lcp` | `number` | `null` | Maximum LCP in milliseconds. |
| `fcp` | `number` | `null` | Maximum FCP in milliseconds. |
| `ttfb` | `number` | `null` | Maximum TTFB in milliseconds. |
| `cls` | `number` | `null` | Maximum CLS score. |
| `inp` | `number` | `null` | Maximum INP in milliseconds. |
| `jsHeapSize` | `number` | `null` | Maximum JS heap size in bytes. |
| `domNodeCount` | `number` | `null` | Maximum DOM node count. |

Setting `enable: true` without any metric thresholds activates metric collection (visible in reporters and the visual runner) but does not enforce any budgets.

### Environment Variables

| Variable | Config Equivalent | Type |
|---|---|---|
| `MOTUS_PERFORMANCE_ENABLE` | `performance.enable` | `bool` |
| `MOTUS_PERFORMANCE_LCP` | `performance.lcp` | `double` |
| `MOTUS_PERFORMANCE_FCP` | `performance.fcp` | `double` |
| `MOTUS_PERFORMANCE_TTFB` | `performance.ttfb` | `double` |
| `MOTUS_PERFORMANCE_CLS` | `performance.cls` | `double` |
| `MOTUS_PERFORMANCE_INP` | `performance.inp` | `double` |
| `MOTUS_PERFORMANCE_JS_HEAP_SIZE` | `performance.jsHeapSize` | `long` |
| `MOTUS_PERFORMANCE_DOM_NODE_COUNT` | `performance.domNodeCount` | `int` |

`collectAfterNavigation` has no environment variable override; use the config file or code for that setting.

---

## Reporter Integration

When performance metrics are collected, all reporters that implement `IPerformanceReporter` receive the metrics alongside any budget evaluation results.

### Built-in Reporter Output

| Reporter | Output |
|---|---|
| **Console** | Prints a metrics table after each test showing Metric, Value, Budget, and Status. Pass/fail is color-coded. |
| **HTML** | Adds a "Performance Metrics" section in the test detail panel with a table showing each metric, value, budget threshold, and pass/fail status. |
| **JUnit** | Emits `<property>` elements on the test case (e.g. `<property name="perf.lcp" value="2345.6" />`). |
| **TRX** | Includes a formatted metrics table in the test result's `Output.StdOut` section. |

### Custom Performance Reporter

Implement both `IReporter` and `IPerformanceReporter` to receive performance events:

```csharp
using Motus.Abstractions;

public sealed class PerfDashboardReporter : IReporter, IPerformanceReporter
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
        Console.WriteLine($"[{test.TestName}] LCP={metrics.Lcp:F0}ms FCP={metrics.Fcp:F0}ms CLS={metrics.Cls:F3}");

        if (budgetResult is { Passed: false })
        {
            foreach (var entry in budgetResult.Entries.Where(e => !e.Passed))
                Console.WriteLine($"  OVER BUDGET: {entry.MetricName} = {entry.ActualValue:F1} (budget: {entry.Threshold:F1}, delta: +{entry.Delta:F1})");
        }

        return Task.CompletedTask;
    }
}
```

Register it via `IPluginContext.RegisterReporter()`. The engine checks `reporter is IPerformanceReporter` at runtime and dispatches performance events only to reporters that opt in.

---

## Visual Runner

When the performance collector is enabled, the visual runner surfaces metrics in two places:

- **Timeline panel**: Navigation markers that have collected performance data show LCP, FCP, and CLS values in their tooltip.
- **Step detail panel**: Selecting a navigation step shows a "Performance" section with a full metrics table (LCP, FCP, TTFB, CLS, INP, JS Heap, DOM Nodes).

---

## Complete Example

```csharp
using Motus.Abstractions;
using Motus.Testing.MSTest;
using static Motus.Assertions.Expect;

[TestClass]
[PerformanceBudget(Lcp = 2500, Fcp = 1800, Cls = 0.1)]
public class PerformanceTests : MotusTestBase
{
    [TestMethod]
    public async Task Homepage_MeetsPerformanceBudget()
    {
        await Page.GotoAsync("https://example.com");
        await That(Page).ToMeetPerformanceBudgetAsync();
    }

    [TestMethod]
    [PerformanceBudget(Lcp = 1500, Inp = 200)]
    public async Task Checkout_HasTighterBudget()
    {
        await Page.GotoAsync("https://example.com/checkout");
        await That(Page).ToMeetPerformanceBudgetAsync();
    }

    [TestMethod]
    public async Task Dashboard_LcpUnder3Seconds()
    {
        await Page.GotoAsync("https://example.com/dashboard");
        await That(Page).ToHaveLcpBelowAsync(3000);
    }

    [TestMethod]
    public async Task Search_NoLayoutShift()
    {
        await Page.GotoAsync("https://example.com/search");
        await That(Page).ToHaveClsBelowAsync(0.05);
    }
}
```

---

## See Also

- [Configuration](./configuration.md)
- [Accessibility Testing](./accessibility-testing.md)
- [Plugin Interfaces](../extensions/plugin-interfaces.md)
- [Testing Frameworks](./testing-frameworks.md)
