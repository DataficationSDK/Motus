# Selectors and Assertions

Motus provides a layered system for locating elements and verifying page state. The selector system routes prefixed strings to pluggable resolution strategies, and the assertion engine wraps those resolutions in a polling retry loop that tolerates transient DOM and protocol errors. Both layers are extensible through the `Motus.Abstractions` plugin interfaces.

---

## Selector System

### SelectorStrategyRegistry

`SelectorStrategyRegistry` (internal, `Motus` namespace) is a thread-safe dictionary that maps strategy names to `ISelectorStrategy` implementations. All reads and writes are guarded by a `lock` on a private `object` field.

Key behaviors:

- `Register(ISelectorStrategy)` stores or overwrites by `strategy.StrategyName` (case-insensitive, `StringComparer.OrdinalIgnoreCase`).
- `TryGetStrategy(string prefix, out ISelectorStrategy?)` returns whether a named strategy exists.
- `GetDefault()` returns the `"css"` strategy unconditionally. This is the fallback used when no prefix is found in a selector string.
- `GetAllByPriority()` returns a snapshot of all registered strategies sorted by `Priority` descending. This ordering drives the recorder's selector inference.

### Built-in Strategies

These strategies are registered at startup. The prefix is the string that appears before `=` in a selector expression (for example, `role=button`).

| Strategy | Prefix | Priority | Resolution mechanism |
|---|---|---|---|
| Backend node ID | `_node` | 100 | CDP `DOM.resolveNode` for a node ID read from an accessibility snapshot |
| TestId | `data-testid` (configurable) | 40 | CSS attribute selector via recursive shadow DOM traversal |
| Role | `role` | 30 | CDP `Accessibility.queryAXTree` with optional name filter |
| Text | `text` | 20 | JavaScript `textContent` match with optional shadow-piercing tree walk |
| CSS | `css` | 10 | `querySelectorAll` with optional recursive shadow DOM traversal |
| XPath | `xpath` | 10 | `document.evaluate` with `ORDERED_NODE_SNAPSHOT_TYPE`; no shadow piercing |
| GetBy text, label, attribute | `_text`, `_label`, `_attr` | 0 | JavaScript text, label and attribute matching behind the `GetBy*` factories; see below |

The prefixes that start with an underscore are reserved for selectors Motus builds itself. They resolve like any other prefix but never generate a selector for the recorder.

### Locator factories

The `GetBy*` methods on `IPage` and `IFrame` build a selector for one of the strategies above:

| Factory | Selector it builds | Matches |
|---|---|---|
| `GetByRole(role, name)` | `role=button[name="Save"]` | The accessibility tree: implicit roles and computed accessible names |
| `GetByText(text, exact)` | `_text="Save"i` | The innermost element whose text matches; `input` buttons by their value |
| `GetByLabel(text, exact)` | `_label="Email"i` | Elements labelled by `aria-labelledby`, `aria-label`, or an associated `<label>` |
| `GetByPlaceholder(text, exact)` | `_attr=placeholder="Search"i` | The `placeholder` attribute |
| `GetByTitle(text, exact)` | `_attr=title="Close"i` | The `title` attribute |
| `GetByAltText(text, exact)` | `_attr=alt="Logo"i` | The `alt` attribute |
| `GetByTestId(id)` | `[data-testid="submit"]` | The `data-testid` attribute, through the CSS strategy |

The text-based factories write their text as a JSON string literal followed by a flag. `i`, used when `exact` is omitted or false, matches a case-insensitive substring. `s`, used when `exact` is true, matches the whole string case-sensitively. Both collapse runs of whitespace and trim the ends before comparing, so indentation in the markup never decides a match. Quoting the text as JSON keeps any quote, bracket or backslash in it from ending the selector early.

`GetByLabel` reads an element's labels in the same order of precedence a browser computes a name from: the elements named by `aria-labelledby` when it resolves to any, otherwise a non-empty `aria-label`, otherwise the `<label>` elements associated with a form control through `for` or by wrapping it.

`GetByRole` matches the accessible name exactly, as `Accessibility.queryAXTree` does. Elements the accessibility tree ignores, such as one inside `display: none` or `aria-hidden="true"`, never match.

### Resolution scope: the page, or one frame

Every strategy resolves against a frame, and which frame that is comes from where the locator was built. A locator from `IPage` is rooted at the main frame; one from `IFrame` is rooted at that frame and resolves in that frame's document only. A selector that would match in two frames matches just the one the locator came from.

The frame root decides two things at once: the execution context the strategy queries in, and the protocol session the query is sent on. For a frame the browser renders in its own process, the session is the frame's own rather than the page's, and every element handle the strategy returns is bound to that session. Neither is visible to the caller, and code written for a same-process frame works unchanged when the browser isolates it.

One consequence is worth stating because it is easy to assume otherwise. A backend node identifier, and the `_node` selector built from it, is only meaningful on the session that reported it. A node read from one frame's accessibility tree has to be addressed through that same frame.

Measurement follows the same root. `ILocator.BoundingBoxAsync` reports page coordinates for a frame-rooted locator, so a box can be handed to `page.Mouse` directly, which is where input is dispatched.

See [Frames and iframes](../guides/frames-and-iframes.md) for the caller-facing view.

### TestId Strategy

`TestIdSelectorStrategy` accepts a configurable attribute name in its constructor (default `"data-testid"`). The `StrategyName` property returns the configured attribute name, so registering the strategy with a different attribute (for example, `"data-cy"`) also changes the prefix used in selector strings.

Resolution builds a CSS attribute selector of the form `[attr="value"]` and, when `pierceShadow` is true, runs it through a recursive `queryShadow` JavaScript function that calls `querySelectorAll('*')` on every discovered shadow root. When `pierceShadow` is false, it falls back to a plain `document.querySelectorAll` call.

`GenerateSelector` reads the configured attribute from the element and returns `attributeName=value`, or `null` if the attribute is absent.

### Role Strategy

`RoleSelectorStrategy` uses the Chrome DevTools Protocol Accessibility domain. It calls `Accessibility.enable` once per page session (tracked with a `volatile bool` field), then calls `Accessibility.queryAXTree` with the extracted role and optional accessible name. Because the CDP accessibility tree already spans shadow boundaries natively, the `pierceShadow` parameter has no additional effect.

Selector syntax supports an optional name filter using bracket notation:

```
role=button[name="Submit"]
```

Parsing is implemented in `ParseRoleSelector(ReadOnlySpan<char>)` without regular expressions. It strips the `role=` prefix, slices at the first `[`, then checks for a `name=` attribute. Quoted names are extracted by skipping the leading `"` and the trailing `"]` pair, so a quoted name may itself contain quotes and brackets. Unquoted names are extracted by slicing to the first `]`. Nodes with `Ignored: true` or no `BackendDOMNodeId` are skipped. Each matching node is resolved to an `ElementHandle` via `DOM.resolveNode`.

The query matches the browser's own spelling of a role. Chromium spells the ARIA `img` role as `image`, so the strategy queries `image` first for either spelling and falls back to `img` when that finds nothing.

`GenerateSelector` evaluates an inline JavaScript function that checks the explicit `role` attribute first, then infers semantic roles from tag name and `type` attribute (for example, `<button>` maps to `button`, `<a href>` maps to `link`, `<input type="checkbox">` maps to `checkbox`). If a role is found it appends the accessible name using `aria-label` or trimmed `textContent`, formatted as `role=button[name="Submit"]`.

### Text Strategy

`TextSelectorStrategy` evaluates a JavaScript expression that scans element `textContent`. Two match modes are supported:

- **Contains** (default): `selector.textContent.includes("value")`
- **Exact**: wrap the value in double quotes in the selector string: `text="exact value"` -- the strategy detects the surrounding quotes and uses strict equality (`textContent.trim() === "value"`)

When `pierceShadow` is true, the JS function `walkShadow` calls `querySelectorAll('*')` on the root and recurses into any discovered `shadowRoot`. When `pierceShadow` is false, a `TreeWalker` limited to `NodeFilter.SHOW_ELEMENT` traverses the main document only.

`GenerateSelector` reads `textContent`, trims whitespace, and returns `text=trimmedValue`. It returns `null` if the content is empty or exceeds 100 characters.

### CSS Strategy

`CssSelectorStrategy` delegates entirely to the browser's native CSS engine. When `pierceShadow` is true, an inline `queryShadow` JavaScript function calls `querySelectorAll('*')` on every element in the tree, then recurses into any `shadowRoot`. When `pierceShadow` is false, it calls `document.querySelectorAll` directly.

`GenerateSelector` evaluates an inline JavaScript function that builds a selector from the element upward. It prefers `#id` selectors, then tag with class names (for example, `div.container.active`), and falls back to `:nth-of-type` sibling disambiguation. The result is prefixed with `css=`.

### XPath Strategy

`XPathSelectorStrategy` calls `document.evaluate` with `XPathResult.ORDERED_NODE_SNAPSHOT_TYPE` and collects results with `snapshotItem`. XPath cannot traverse shadow DOM boundaries; this is a language-level constraint and not a limitation of the strategy implementation. The `pierceShadow` parameter is accepted for interface compatibility but ignored.

`GenerateSelector` walks from the element to `document.body`, building path segments as `/tagName[n]` using sibling index, resulting in expressions such as `xpath=/html/body/div[2]/button[1]`.

### Selector Inference for Recording and Codegen

The recorder ranks all registered strategies by `Priority` descending using `GetAllByPriority()`. For each element, it calls `GenerateSelector` on each strategy in priority order, skipping any that return `null`. The first candidate is then verified for uniqueness by calling `ResolveAsync` and checking that exactly one element is returned. If the candidate is not unique, the next strategy in priority order is tried. CSS is the final fallback.

The `ISelectorStrategy` interface contract for `GenerateSelector` is:

```csharp
Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default);
```

---

## Assertion Engine

### Expect Static Class

`Expect` is the entry point for all assertions. It provides three factory overloads:

```csharp
public static class Expect
{
    public static LocatorAssertions That(ILocator locator);
    public static PageAssertions     That(IPage page);
    public static ResponseAssertions That(IResponse response);
}
```

Each overload downcasts the abstraction to its concrete type and returns the appropriate assertions object. Assertions are only available in-process; the downcasts are safe because all Motus implementations are internal to the library.

### AssertionRetryHelper Polling Loop

`AssertionRetryHelper.RetryUntilAsync` is the shared retry mechanism used by `LocatorAssertions` and `PageAssertions`. It is internal to the `Motus.Assertions` namespace.

Behavior:

- **Polling interval**: 100 ms (constant `PollingIntervalMs`).
- **Default timeout**: resolved by `ResolveTimeout(int?)`. A per-call timeout from `AssertionOptions.Timeout` takes precedence. If no per-call timeout is supplied, it falls back to the value at `MotusConfigLoader.Config.Assertions?.Timeout`, and if that is also absent, uses 10,000 ms.
- A linked `CancellationTokenSource` is created from the caller's token and cancelled after the timeout via `CancelAfter`.
- On each iteration, if the condition delegate returns `(passed: true, actual)`, the assertion returns successfully.
- **Transient error handling**: the following exception types are caught and retried rather than propagated: `InvalidOperationException` (evaluation failures), `MotusProtocolException` (CDP command errors such as stale context), `MotusTargetClosedException` (target navigated away), and `TimeoutException` (inner operation timeouts). `OperationCanceledException` is not caught here and rethrows to the outer handler.
- When the timeout elapses, `OperationCanceledException` is caught and rethrown as `MotusAssertionException` with full diagnostic context.

### Element absence

`LocatorAssertions` decides what a locator that matches nothing means before the helper sees a result. Each assertion passes a `whenAbsent` value to its private `RetryAsync`, which catches `ElementNotFoundException` from the condition and turns it into a poll result with `<element not found>` as the received value:

- `whenAbsent: false` (`ToBeVisibleAsync`): absence means the condition does not hold. The assertion keeps polling for the element to appear, and `Not.ToBeVisibleAsync` passes at once.
- `whenAbsent: true` (`ToBeHiddenAsync`): absence means the condition holds. The assertion passes at once, and `Not.ToBeHiddenAsync` keeps polling for a visible element.
- `whenAbsent: null` (every other element assertion): absence answers nothing. The result reported is the assertion's own polarity, so the helper's negation comes out unsatisfied for both forms and the poll repeats until the element appears or the timeout elapses. This follows Playwright: `Not.ToHaveTextAsync("x")` on a missing element fails rather than passing.
- `ToBeAttachedAsync`, `ToBeDetachedAsync`, and `ToHaveCountAsync` count matches instead of resolving one element, so a missing element is simply a count of zero.

| Assertion | Nothing matches | `Not` form, nothing matches |
|---|---|---|
| `ToBeVisibleAsync` | waits | passes |
| `ToBeHiddenAsync` | passes | waits |
| `ToBeAttachedAsync` | waits | passes |
| `ToBeDetachedAsync` | passes | waits |
| `ToHaveCountAsync(0)` | passes | waits |
| `ToHaveCountAsync(n > 0)` | waits | passes |
| All other locator assertions | waits | waits |

A wait that runs out fails with `MotusAssertionException` whose `Actual` is `<element not found>`.

### .Not Negation

All three assertion classes expose a `Not` property that returns a new instance of the same class with the `_negate` field flipped. The polarity is applied in `RetryUntilAsync` by inverting the `passed` result from the condition delegate before checking for success. Failure messages prefix the expected value with `"NOT "`.

```csharp
await Expect.That(locator).Not.ToBeVisibleAsync();
```

### LocatorAssertions Methods

All methods accept an optional `AssertionOptions? options` parameter and return `Task`.

| Method | Condition checked |
|---|---|
| `ToBeVisibleAsync` | Element is visible (`IsVisibleAsync`); a missing element is not visible |
| `ToBeHiddenAsync` | Element is hidden (`IsHiddenAsync`); a missing element is hidden |
| `ToBeEnabledAsync` | Element is enabled (`IsEnabledAsync`) |
| `ToBeDisabledAsync` | Element is disabled (`IsDisabledAsync`) |
| `ToBeCheckedAsync` | Checkbox or radio is checked (`IsCheckedAsync`) |
| `ToBeEditableAsync` | Element is editable (`IsEditableAsync`) |
| `ToBeEmptyAsync` | Element has no value or content (`IsEmptyAsync`) |
| `ToBeAttachedAsync` | At least one matching element exists in the DOM (`CountAsync > 0`); `MotusSelectorException` is treated as count = 0 |
| `ToBeDetachedAsync` | No matching elements exist in the DOM (`CountAsync == 0`); `MotusSelectorException` is treated as count = 0 |
| `ToHaveTextAsync(string)` | `textContent` equals expected (exact match) |
| `ToHaveTextAsync(Regex)` | `textContent` matches the regular expression |
| `ToContainTextAsync(string)` | `textContent` contains the expected substring (ordinal) |
| `ToHaveValueAsync(string)` | Input value equals expected |
| `ToHaveAttributeAsync(string, string)` | Named attribute equals expected value |
| `ToHaveClassAsync(string)` | Element has the specified CSS class name |
| `ToHaveCSSAsync(string, string)` | Computed style property equals expected value |
| `ToHaveCountAsync(int)` | Number of matched elements equals expected count |
| `ToHaveAccessibleNameAsync(string)` | Accessible name equals expected |
| `ToHaveRoleAsync(string)` | Accessible role equals expected |

### PageAssertions Methods

| Method | Condition checked |
|---|---|
| `ToHaveUrlAsync(string)` | Current page URL equals the string, or matches it as a glob over the whole URL when it contains `*` (via `Page.UrlMatchesWhole`, the same matcher `WaitForURLAsync` uses). A string without a scheme is resolved against the context's `BaseURL` first when one is set. There is no substring match. |
| `ToHaveTitleAsync(string)` | `document.title` equals expected (via `TitleAsync`) |
| `ToPassAccessibilityAuditAsync(Action<AccessibilityAssertionOptions>?, AssertionOptions?)` | No accessibility violations; see below |

`ToPassAccessibilityAuditAsync` first checks `_page.LastAccessibilityAudit`. If no cached result exists (no audit hook has run), it runs an on-demand audit by calling `_page.RunAccessibilityAuditAsync` with the currently registered rules filtered by `SkippedRules`. Violations are then filtered by `SkippedRules` and by `IncludeWarnings` on `AccessibilityAssertionOptions`. When `IncludeWarnings` is false (the default), only `AccessibilityViolationSeverity.Error` violations are considered. This assertion does not use the retry loop; it evaluates once and throws immediately on failure.

`PageAssertions.Not` inverts the check: `Not.ToPassAccessibilityAuditAsync` asserts that at least one violation exists.

### ResponseAssertions Methods

`ResponseAssertions` does not use the retry loop. Both methods evaluate synchronously and throw immediately.

| Method | Condition checked |
|---|---|
| `ToBeOkAsync()` | `IResponse.Ok` is true (status in range 200-299) |
| `ToHaveStatusAsync(int)` | `IResponse.Status` equals the exact expected code |

### MotusAssertionException Detail

When an assertion fails (either after timeout or immediately for response assertions), a `MotusAssertionException` is thrown. The exception captures:

- `expected` - the expected value or condition description, prefixed with `"NOT "` when negated
- `actual` - the last observed value from the condition delegate (or the response status as a string)
- `selector` - the locator's selector string, or `null` for page and response assertions
- `pageUrl` - the current page URL at the time of failure, or the response URL for response assertions
- `assertionTimeout` - the `TimeSpan` of the configured timeout; `TimeSpan.Zero` for immediately-evaluated assertions
- `message` - a human-readable summary including all of the above, unless overridden by `AssertionOptions.Message`

---

## Wait Conditions

`IWaitCondition` defines a custom named condition for use with the `WaitForAsync` API.

```csharp
public interface IWaitCondition
{
    string ConditionName { get; }
    Task<bool> EvaluateAsync(IPage page, WaitConditionOptions? options = null);
}
```

`ConditionName` is the string used in wait expressions, for example `"animation-complete"`. `EvaluateAsync` is called repeatedly on a polling interval until it returns `true` or the configured timeout elapses. Passing `null` for `options` causes the engine to use its default polling interval and timeout.

Custom conditions are registered with the plugin system alongside custom selector strategies, following the same registry pattern.

---

## See Also

- `Motus.Abstractions/Plugins/ISelectorStrategy.cs` - full interface contract for custom selector strategies
- `Motus.Abstractions/Plugins/IWaitCondition.cs` - full interface contract for custom wait conditions
- `docs/architecture/overview.md` - plugin registration and extensibility model
- `docs/architecture/browser-lifecycle.md` - frame and page lifecycle relevant to selector resolution scope
- `docs/guides/frames-and-iframes.md` - frame-rooted locators from the caller's side
- `docs/architecture/transport-and-protocol.md` - how a frame reaches the session that owns it
