# Accessibility Testing

Motus includes a first-class accessibility auditing system that checks pages against a built-in set of WCAG 2.1 AA rules without injecting any JavaScript into the page under test. Audits can run automatically as a lifecycle hook after navigation and user actions, on demand inside individual tests via `ToPassAccessibilityAuditAsync`, or as a combination of both. Violations are surfaced through assertions, the reporter pipeline, and optionally through the CLI `--a11y` flag.

---

## Overview

Motus performs accessibility audits using the Chrome DevTools Protocol `Accessibility.getFullAXTree` call. The full accessibility tree is fetched over CDP and walked in-process; no audit scripts are injected into the page. This approach avoids interference with the application under test and works correctly with pages that use Content Security Policy headers that would block injected scripts.

Each node in the tree is passed to every registered `IAccessibilityRule`. Rules return an `AccessibilityViolation` record when the node fails, or `null` when the rule does not apply to that node. Violations carry a severity of `Error` or `Warning`. Rules that check page-level properties (such as the presence of a `<main>` landmark or duplicate `id` attributes) fire once by checking whether they are evaluating the first node in the tree.

---

## Built-in WCAG Rules

Nine rules are registered automatically when the Motus engine initializes. Each rule ID is stable and can be used in `SkipRules` lists.

| Rule ID | Description | Severity |
|---|---|---|
| `a11y-alt-text` | Images must have a non-empty accessible name (alt text or aria-label). | Error |
| `a11y-empty-button` | Buttons must have a non-empty accessible name. | Error |
| `a11y-empty-link` | Links must have a non-empty accessible name. | Error |
| `a11y-unlabeled-form-control` | Form controls must have a non-empty accessible name (label, aria-label, or aria-labelledby). | Error |
| `a11y-color-contrast` | Text elements must have sufficient color contrast (4.5:1 for normal text, 3:1 for large text). | Error |
| `a11y-duplicate-id` | Each id attribute value must be unique within the document. | Error |
| `a11y-missing-lang` | The `<html>` element must have a lang attribute to identify the document language. | Error |
| `a11y-missing-landmark` | Pages should have at least one main landmark for screen reader navigation. | Warning |
| `a11y-heading-hierarchy` | Heading levels should not skip (e.g., h1 followed by h3 without an h2). | Warning |

---

## Page-Level Assertions

`ToPassAccessibilityAuditAsync` is available on `PageAssertions` and checks that the current page produces no violations. When the `AccessibilityAuditHook` is active, the assertion reuses the most recent cached audit result. When the hook is not active, the assertion runs an on-demand audit against the live page.

### Basic usage

```csharp
await Expect.That(Page).ToPassAccessibilityAuditAsync();
```

### Skipping rules

Pass a configuration delegate to `ToPassAccessibilityAuditAsync` to skip specific rules for the duration of that assertion. The `SkipRules` method accepts one or more rule IDs and returns the options object for chaining.

```csharp
await Expect.That(Page).ToPassAccessibilityAuditAsync(opts =>
{
    opts.SkipRules("a11y-color-contrast", "a11y-missing-landmark");
});
```

### Excluding warnings

By default, `IncludeWarnings` is `true`, meaning both `Error` and `Warning` severity violations cause the assertion to fail. Set `IncludeWarnings` to `false` to treat the audit as passing when only warning-severity violations remain.

```csharp
await Expect.That(Page).ToPassAccessibilityAuditAsync(opts =>
{
    opts.IncludeWarnings = false;
});
```

### AccessibilityAssertionOptions reference

| Member | Type | Default | Description |
|---|---|---|---|
| `SkipRules(params string[] ruleIds)` | method | | Excludes the specified rule IDs from pass/fail evaluation. Chainable. |
| `IncludeWarnings` | `bool` | `true` | When `true`, warning-severity violations count as failures alongside errors. |

---

## Element-Level Assertions

`LocatorAssertions` exposes two accessibility-focused assertions that query the CDP accessibility tree for the resolved element rather than inspecting DOM attributes directly.

### ToHaveAccessibleNameAsync

Asserts that the element's computed accessible name (as reported by the accessibility tree) equals the expected string.

```csharp
// The button's text content resolves to its accessible name
await Expect.That(Page.Locator("#submit")).ToHaveAccessibleNameAsync("Submit order");

// A form input with an associated <label> element
await Expect.That(Page.Locator("#email")).ToHaveAccessibleNameAsync("Email address");
```

### ToHaveRoleAsync

Asserts that the element's ARIA role equals the expected string.

```csharp
await Expect.That(Page.Locator("nav")).ToHaveRoleAsync("navigation");
await Expect.That(Page.Locator("main")).ToHaveRoleAsync("main");
```

---

## .Not Negation

All accessibility assertions support `.Not` to invert the expected outcome. Use `.Not` to assert that a page actively contains violations (for example, to verify that a test fixture reliably triggers the rules you intend to exercise), or to assert that an element does not carry a specific accessible name.

```csharp
// Asserts the page has at least one violation
await Expect.That(Page).Not.ToPassAccessibilityAuditAsync();

// Asserts the element's accessible name is not "Submit"
await Expect.That(Page.Locator("#empty-btn")).Not.ToHaveAccessibleNameAsync("Submit");

// Asserts the element does not have the button role
await Expect.That(Page.Locator("div.card")).Not.ToHaveRoleAsync("button");
```

---

## AccessibilityAuditHook

`AccessibilityAuditHook` is an internal `IPlugin` and `ILifecycleHook` that runs audits automatically during test execution. It is disabled by default and must be enabled through `AccessibilityOptions`.

### When it runs

| Trigger | Controlled by | Default |
|---|---|---|
| After each page navigation | `AuditAfterNavigation` | `true` |
| After `click`, `fill`, `selectOption` | `AuditAfterActions` | `false` |

When a trigger fires, the hook calls `Accessibility.getFullAXTree` over CDP, evaluates all registered rules, stores the result on the page as `LastAccessibilityAudit`, and dispatches each violation to both `AccessibilityViolationSink` (used by the CLI test runner) and to any `IAccessibilityReporter` implementations registered on the browser context.

### Enabling the hook in code

Pass an `AccessibilityOptions` record to `LaunchOptions.Accessibility` when constructing the browser.

```csharp
var options = new LaunchOptions
{
    Accessibility = new AccessibilityOptions
    {
        Enable = true,
        Mode = AccessibilityMode.Enforce,
        AuditAfterNavigation = true,
        AuditAfterActions = false,
        IncludeWarnings = true,
        SkipRules = ["a11y-color-contrast"]
    }
};
```

### AccessibilityOptions reference

| Property | Type | Default | Description |
|---|---|---|---|
| `Enable` | `bool` | `false` | Master switch for the audit hook. |
| `Mode` | `AccessibilityMode` | `Enforce` | `Off` disables auditing. `Warn` logs violations without failing tests. `Enforce` fails tests that accumulate error-severity violations. |
| `AuditAfterNavigation` | `bool` | `true` | Run an audit after each navigation. |
| `AuditAfterActions` | `bool` | `false` | Run an audit after `click`, `fill`, and `selectOption`. |
| `IncludeWarnings` | `bool` | `true` | Count warning-severity findings as failures when `Mode` is `Enforce`. |
| `SkipRules` | `IReadOnlyList<string>?` | `null` | Rule IDs excluded from every audit. |

---

## CLI Integration

The `motus run` command accepts an `--a11y` flag that enables the audit hook and sets the violation handling mode for the entire test run. When `--a11y` is provided, the CLI sets the `MOTUS_ACCESSIBILITY_ENABLE` and `MOTUS_ACCESSIBILITY_MODE` environment variables before test execution begins.

### Warn mode

Log violations inline with test output without changing pass/fail outcomes. Use this mode to introduce accessibility coverage incrementally without breaking an existing build.

```sh
motus run MyTests.dll --a11y warn
```

### Enforce mode

Fail any test that accumulates one or more `Error`-severity violations detected by the hook. Tests that already fail for other reasons are not double-counted.

```sh
motus run MyTests.dll --a11y enforce
```

When `--a11y` is omitted, the audit hook is not activated by the CLI regardless of any `motus.config.json` setting. To enable the hook through the config file, set `accessibility.enable` to `true` and omit the CLI flag entirely.

---

## motus.config.json Accessibility Section

The `accessibility` section of `motus.config.json` maps directly to `AccessibilityOptions`. All properties are optional; unset properties retain their defaults.

```json
{
  "accessibility": {
    "enable": true,
    "mode": "Enforce",
    "auditAfterNavigation": true,
    "auditAfterActions": false,
    "includeWarnings": true,
    "skipRules": ["a11y-color-contrast"]
  }
}
```

| Property | Type | Default | Description |
|---|---|---|---|
| `enable` | `bool` | `false` | Master switch for the accessibility audit hook. |
| `mode` | `string` | `"Enforce"` | Violation handling mode. Accepted values: `Off`, `Warn`, `Enforce`. |
| `auditAfterNavigation` | `bool` | `true` | Run an audit after each page navigation. |
| `auditAfterActions` | `bool` | `false` | Run an audit after mutating actions such as click, fill, and selectOption. |
| `includeWarnings` | `bool` | `true` | Treat warning-severity findings as failures alongside errors when mode is `Enforce`. |
| `skipRules` | `string[]` | `null` | Rule IDs excluded from every audit. |

Environment variables `MOTUS_ACCESSIBILITY_ENABLE` and `MOTUS_ACCESSIBILITY_MODE` override the config file values and are themselves overridden by `LaunchOptions.Accessibility` set in code.

---

## Writing Custom Rules

Implement `IAccessibilityRule` from `Motus.Abstractions` to add your own WCAG checks. The engine calls `Evaluate` once per node per rule during each audit pass. Implementations must be synchronous; use `AccessibilityAuditContext` for any data that requires a page-wide view (computed styles, duplicate IDs, document language).

### Implementing the interface

```csharp
using Motus.Abstractions;

public sealed class AriaExpandedOnDisclosureRule : IAccessibilityRule
{
    public string RuleId => "custom-aria-expanded-disclosure";

    public string Description =>
        "Disclosure widgets must expose aria-expanded on their trigger button.";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        if (!string.Equals(node.Role, "button", StringComparison.OrdinalIgnoreCase))
            return null;

        // Only target buttons that control another element
        if (!node.Properties.ContainsKey("controls"))
            return null;

        if (node.Properties.ContainsKey("expanded"))
            return null;

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Error,
            Message: "Disclosure button must expose aria-expanded.",
            NodeRole: node.Role,
            NodeName: node.Name,
            BackendDOMNodeId: node.BackendDOMNodeId,
            Selector: null);
    }
}
```

### Registering via IPluginContext

Register the rule inside your plugin's `OnLoadedAsync` implementation. The rule is added to the engine's rule list and participates in all subsequent audits for the lifetime of the browser context.

```csharp
using Motus.Abstractions;

public sealed class AccessibilityExtensionsPlugin : IPlugin
{
    public string PluginId => "my-org.accessibility-extensions";
    public string Name => "Accessibility Extensions";
    public string Version => "1.0.0";
    public string? Author => "My Org";
    public string? Description => "Custom WCAG rules for our design system.";

    public Task OnLoadedAsync(IPluginContext context)
    {
        context.RegisterAccessibilityRule(new AriaExpandedOnDisclosureRule());
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync() => Task.CompletedTask;
}
```

Pass the plugin to `LaunchOptions.Plugins` before creating the browser:

```csharp
var options = new LaunchOptions
{
    Plugins = [new AccessibilityExtensionsPlugin()],
    Accessibility = new AccessibilityOptions { Enable = true }
};
```

---

## Reporter Integration

All four built-in reporters implement both `IReporter` and `IAccessibilityReporter`. Violations are dispatched to each reporter that implements `IAccessibilityReporter` before `OnTestEndAsync` is called for the same test.

### Console reporter

Violations appear inline below the test result line, color-coded by severity. `Error` violations are printed in red; `Warning` violations are printed in yellow.

```
  [FAIL] HomePage_MeetsAccessibilityStandards (312ms)
         [A11Y Error] a11y-alt-text: Image has no accessible name. Add alt text or aria-label.
         [A11Y Warning] a11y-missing-landmark: Page has no main landmark.
```

### HTML reporter

Violations are collected per test and rendered as a dedicated accessibility findings table within the test's row in the HTML report. The table shows rule ID, severity, message, and selector.

### JUnit reporter

Violations are collected per test and appended to the `<failure>` or `<system-out>` element for the corresponding `<testcase>` entry.

### TRX reporter

Tests that received at least one violation are tracked in a set. The outcome of those tests reflects any failure caused by enforcement mode; the violation detail is available through the console or HTML reporters in the same run.

### Custom reporter with accessibility support

Implement both `IReporter` and `IAccessibilityReporter` to receive violation events in a custom reporter. The `reporter is IAccessibilityReporter` check is performed at runtime; no base class or attribute is needed.

```csharp
using Motus.Abstractions;

public sealed class AccessibilityAuditFileReporter : IReporter, IAccessibilityReporter
{
    private readonly string _outputPath;
    private readonly List<string> _lines = [];

    public AccessibilityAuditFileReporter(string outputPath)
    {
        _outputPath = outputPath;
    }

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;
    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;
    public Task OnTestRunEndAsync(TestRunSummary summary) =>
        File.WriteAllLinesAsync(_outputPath, _lines);

    public Task OnTestEndAsync(TestInfo test, TestResult result) => Task.CompletedTask;

    public Task OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)
    {
        _lines.Add(
            $"[{violation.Severity}] {violation.RuleId} | {test.TestName} | {violation.Message}");
        return Task.CompletedTask;
    }
}
```

---

## Complete Example Test Class

The following class demonstrates page-level audits, rule skipping, warnings-only mode, element-level assertions, and `.Not` negation in a single self-contained test file.

```csharp
using Motus;
using Motus.Assertions;

[TestClass]
public class HomePageAccessibilityTests : MotusTestBase
{
    [TestMethod]
    public async Task HomePage_PassesAccessibilityAudit()
    {
        await Page.GotoAsync("https://example.internal/");

        await Expect.That(Page).ToPassAccessibilityAuditAsync();
    }

    [TestMethod]
    public async Task HomePage_PassesAudit_IgnoringColorContrast()
    {
        await Page.GotoAsync("https://example.internal/");

        // Color contrast is handled by a separate visual review process.
        await Expect.That(Page).ToPassAccessibilityAuditAsync(opts =>
        {
            opts.SkipRules("a11y-color-contrast");
        });
    }

    [TestMethod]
    public async Task HomePage_PassesAudit_ErrorsOnly()
    {
        await Page.GotoAsync("https://example.internal/");

        // Warnings tracked separately; only errors block this check.
        await Expect.That(Page).ToPassAccessibilityAuditAsync(opts =>
        {
            opts.IncludeWarnings = false;
        });
    }

    [TestMethod]
    public async Task SearchInput_HasAccessibleLabel()
    {
        await Page.GotoAsync("https://example.internal/");

        await Expect.That(Page.Locator("[data-testid='search-input']"))
            .ToHaveAccessibleNameAsync("Search");
    }

    [TestMethod]
    public async Task PrimaryNav_HasNavigationRole()
    {
        await Page.GotoAsync("https://example.internal/");

        await Expect.That(Page.Locator("header nav"))
            .ToHaveRoleAsync("navigation");
    }

    [TestMethod]
    public async Task IconButton_DoesNotHaveUnintendedLabel()
    {
        await Page.GotoAsync("https://example.internal/");

        // An icon-only button should expose a meaningful label via aria-label,
        // not expose the raw icon character as its accessible name.
        await Expect.That(Page.Locator("[data-testid='close-btn']"))
            .Not.ToHaveAccessibleNameAsync("×");
    }

    [TestMethod]
    public async Task KnownViolationPage_FailsAudit()
    {
        // Navigate to a fixture page that is intentionally non-compliant.
        await Page.GotoAsync("https://example.internal/test-fixtures/a11y-violations");

        // Verify the fixture is actually broken before relying on it for other tests.
        await Expect.That(Page).Not.ToPassAccessibilityAuditAsync();
    }
}
```

---

## See Also

- [Configuration](./configuration.md) -- full `motus.config.json` schema and `AccessibilityOptions` property reference
- [Plugin Interfaces](../extensions/plugin-interfaces.md) -- `IAccessibilityRule`, `IAccessibilityReporter`, and `IPluginContext` API reference
