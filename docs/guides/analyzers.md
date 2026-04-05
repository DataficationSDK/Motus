# Roslyn Analyzers

The `Motus.Analyzers` package ships seven Roslyn diagnostic rules that catch common mistakes in Motus-based test projects at compile time. Rules in the `Motus.Automation` category indicate correctness problems that are likely to produce silent failures or resource leaks. Rules in the `Motus.Usage` category flag style and maintainability issues.

Three of the rules include IDE code fixes that apply the correction automatically.

---

## Installation

Add the package to your test project. Because it is a Roslyn analyzer it must be referenced with `PrivateAssets="all"` so that it is not propagated to consumers of your project.

```xml
<ItemGroup>
  <PackageReference Include="Motus.Analyzers" Version="*">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

The analyzers target `netstandard2.0` and are compatible with any project that references `Microsoft.CodeAnalysis.CSharp` 4.x or later.

---

## Rules

### MOT001 - Async Motus call is not awaited

| Property | Value |
|---|---|
| Rule ID | `MOT001` |
| Severity | Warning |
| Category | `Motus.Automation` |
| Code fix | Yes - "Add await" |

**What it detects.** An invocation of an async method on a Motus interface (`IPage`, `ILocator`, `IBrowser`, `IBrowserContext`, or `IFrame`) that appears as a standalone expression statement without `await`. The returned `Task` is silently discarded, so the browser action never completes before the next statement runs.

**Example - flagged code:**

```csharp
page.ClickAsync("#submit");           // MOT001
page.Locator("#name").FillAsync("x"); // MOT001
```

**Example - after applying the code fix:**

```csharp
await page.ClickAsync("#submit");
await page.Locator("#name").FillAsync("x");
```

The code fix also adds the `async` modifier to the enclosing method if it is missing.

---

### MOT002 - Avoid hardcoded delays in browser tests

| Property | Value |
|---|---|
| Rule ID | `MOT002` |
| Severity | Info |
| Category | `Motus.Usage` |
| Code fix | Yes - "Replace with WaitForLoadStateAsync" |

**What it detects.** Any call to `Task.Delay` or `Thread.Sleep` inside a test method. Hardcoded delays make tests slow and unreliable. The browser may not have finished loading when the delay expires, or the delay may be far longer than necessary.

**Example - flagged code:**

```csharp
await page.GotoAsync("https://example.com");
await Task.Delay(2000);                         // MOT002
Assert.IsTrue(await page.IsVisibleAsync("#hero"));
```

**Example - after applying the code fix:**

```csharp
await page.GotoAsync("https://example.com");
await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
Assert.IsTrue(await page.IsVisibleAsync("#hero"));
```

The code fix performs a best-effort search for an `IPage` variable or parameter in scope to use as the receiver. Review the emitted call and adjust the `LoadState` argument if a different wait condition is more appropriate for your scenario.

---

### MOT003 - Selector appears fragile

| Property | Value |
|---|---|
| Rule ID | `MOT003` |
| Severity | Info |
| Category | `Motus.Usage` |
| Code fix | No |

**What it detects.** A string literal passed to `Locator(...)` that matches one of three fragility patterns:

- **Deeply nested selector** - four or more descendant combinators, e.g. `div > ul > li > a > span`.
- **Chained `:nth-child` selectors** - two or more `:nth-child(...)` pseudo-classes in the same selector expression.
- **Auto-generated class name** - a class token that matches the pattern `word-hexhex` (five to eight hex digits), which is characteristic of CSS Modules and CSS-in-JS hashes.

**Example - flagged code:**

```csharp
page.Locator("div > ul > li > a > span");               // MOT003: deeply nested selector
page.Locator(".header:nth-child(2) .item:nth-child(1)"); // MOT003: chained :nth-child selectors
page.Locator(".button-3fa2c1e8");                        // MOT003: auto-generated class name
```

**Recommended action.** Replace fragile selectors with semantic alternatives:

```csharp
page.GetByRole(AriaRole.Link, new() { Name = "Details" });
page.GetByTestId("submit-button");
page.GetByLabel("Email address");
```

---

### MOT004 - Browser or context not disposed with await using

| Property | Value |
|---|---|
| Rule ID | `MOT004` |
| Severity | Warning |
| Category | `Motus.Automation` |
| Code fix | Yes - "Add await using" |

**What it detects.** A local variable declaration whose initializer produces an `IBrowser` or `IBrowserContext` instance that is not declared with both the `await` and `using` keywords. Omitting `await using` means the browser process may not be terminated if the test throws.

**Example - flagged code:**

```csharp
var browser = await MotusLauncher.LaunchAsync(options);  // MOT004
var context = await browser.NewContextAsync();            // MOT004
```

**Example - after applying the code fix:**

```csharp
await using var browser = await MotusLauncher.LaunchAsync(options);
await using var context = await browser.NewContextAsync();
```

---

### MOT005 - Locator result is not used

| Property | Value |
|---|---|
| Rule ID | `MOT005` |
| Severity | Warning |
| Category | `Motus.Automation` |
| Code fix | No |

**What it detects.** A call to a locator-creating method (`Locator`, `GetByRole`, `GetByText`, `GetByLabel`, `GetByPlaceholder`, `GetByTestId`, `GetByTitle`, `GetByAltText`, `First`, `Last`, `Nth`, or `Filter`) on an `IPage`, `ILocator`, or `IFrame` where the result is discarded. Locators are lazy and have no observable effect unless an action or assertion is called on them.

**Example - flagged code:**

```csharp
page.Locator("#username");            // MOT005
page.GetByRole(AriaRole.Button);      // MOT005
```

**Recommended action.** Either chain an action onto the locator call or assign the result to a variable for later use:

```csharp
await page.Locator("#username").FillAsync("alice");
var submitButton = page.GetByRole(AriaRole.Button, new() { Name = "Submit" });
await submitButton.ClickAsync();
```

---

### MOT006 - Selector uses deprecated engine prefix

| Property | Value |
|---|---|
| Rule ID | `MOT006` |
| Severity | Info |
| Category | `Motus.Usage` |
| Code fix | No |

**What it detects.** A string literal passed to `Locator(...)` that begins with one of the deprecated engine prefix tokens: `css=`, `xpath=`, `text=`, or `id=`. These prefixes are a legacy selector engine syntax. Modern Motus code should use the dedicated locator methods or bare CSS/XPath without a prefix.

**Example - flagged code:**

```csharp
page.Locator("css=#main-nav");           // MOT006
page.Locator("xpath=//button[@id='x']"); // MOT006
page.Locator("text=Sign in");            // MOT006
page.Locator("id=username");             // MOT006
```

**Recommended action.** Remove the prefix or migrate to the appropriate dedicated method:

```csharp
page.Locator("#main-nav");
page.Locator("//button[@id='x']");
page.GetByText("Sign in");
page.Locator("#username");
```

---

### MOT007 - Navigation not followed by a wait

| Property | Value |
|---|---|
| Rule ID | `MOT007` |
| Severity | Warning |
| Category | `Motus.Automation` |
| Code fix | No |

**What it detects.** A call to a navigation method (`GotoAsync`, `GoBackAsync`, `GoForwardAsync`, or `ReloadAsync`) on an `IPage` or `IFrame` that is not immediately followed by one of the recognized wait methods: `WaitForLoadStateAsync`, `WaitForURLAsync`, `WaitForRequestAsync`, `WaitForResponseAsync`, `WaitForFunctionAsync`, or `WaitForTimeoutAsync`. Beginning to interact with the page before it has finished loading is a frequent source of flaky tests.

**Example - flagged code:**

```csharp
await page.GotoAsync("https://example.com/dashboard"); // MOT007
await page.Locator("#welcome").ClickAsync();
```

**Recommended action.** Add an explicit wait immediately after the navigation call:

```csharp
await page.GotoAsync("https://example.com/dashboard");
await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
await page.Locator("#welcome").ClickAsync();
```

---

## Code fixes summary

| Rule | Code fix title | What it does |
|---|---|---|
| MOT001 | Add await | Wraps the invocation in `await` and adds `async` to the enclosing method if needed |
| MOT002 | Replace with WaitForLoadStateAsync | Replaces `Task.Delay` or `Thread.Sleep` with `await page.WaitForLoadStateAsync(LoadState.NetworkIdle)` |
| MOT004 | Add await using | Adds both the `await` and `using` keywords to the local variable declaration |

All three code fixes support **Fix All** via the batch fixer, so you can apply a fix across an entire document, project, or solution in one step.

---

## Suppression

To suppress a diagnostic on a single line, use a `#pragma` directive or the `[SuppressMessage]` attribute.

**Pragma suppression:**

```csharp
#pragma warning disable MOT002
await Task.Delay(500); // intentional: waiting for animation
#pragma warning restore MOT002
```

**Attribute suppression:**

```csharp
[System.Diagnostics.CodeAnalysis.SuppressMessage("Motus.Usage", "MOT002:Avoid hardcoded delays in browser tests",
    Justification = "Animation requires a fixed delay before screenshot.")]
public async Task TakeAnimationScreenshot()
{
    await page.GotoAsync(url);
    await Task.Delay(600);
    await page.ScreenshotAsync(new() { Path = "shot.png" });
}
```

To disable a rule for an entire project, add it to `<NoWarn>` in the `.csproj`:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);MOT003;MOT006</NoWarn>
</PropertyGroup>
```

To change the severity of a rule without disabling it entirely, use an `.editorconfig` entry:

```ini
[*.cs]
dotnet_diagnostic.MOT007.severity = suggestion
```

---

## See also

- [Configuration](configuration.md) - configuring browser launch options that affect disposal patterns
- [Testing Frameworks](testing-frameworks.md) - `MotusTestBase` and fixture lifecycle, which MOT004 complements
- [Recording and Code Generation](recording-and-codegen.md) - generated code already conforms to all analyzer rules
