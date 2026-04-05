# Migrating from Playwright for .NET

Motus is a browser automation framework for .NET that covers the same core scenarios as Playwright for .NET: navigation, element interaction, network interception, screenshots, tracing, and assertions. The two APIs are deliberately close so that existing test suites can be ported with minimal mechanical effort.

This guide covers the differences you will encounter and provides side-by-side mappings for every major API surface.

---

## Key Differences

### No Node.js sidecar

Playwright for .NET ships a bundled Node.js process (`playwright.ps1` / `playwright`) that acts as the automation server; your .NET process talks to it over a named-pipe IPC channel. Motus has no such sidecar. Your .NET process connects directly to the browser over a WebSocket, removing an entire process from the chain and eliminating the Node.js runtime dependency entirely.

### Direct CDP / BiDi protocol

Because Motus connects directly to the browser, every command is a raw Chrome DevTools Protocol (CDP) message for Chromium-family browsers, or a WebDriver BiDi message for Firefox. There is no intermediate translation layer. This makes network traces easier to read and allows you to send arbitrary protocol commands without leaving the Motus API.

### Plugin-based extensibility

Motus exposes an `IPluginContext` on every `IBrowserContext` (via `context.GetPluginContext()`). Plugins can register:

- `ISelectorStrategy` - custom selector engines (equivalent to Playwright's `selectors.register`)
- `ILifecycleHook` - callbacks fired around page creation, navigation, and context teardown
- `INetworkInterceptor` - context-scoped request/response hooks beyond simple `RouteAsync` patterns

Playwright's extensibility is limited to JavaScript-side selector engines and browser context options; Motus hooks are pure .NET.

### Source-generated protocol bindings for NativeAOT

Motus serializes all CDP/BiDi messages using `System.Text.Json` source generation. There are no reflection-based JSON converters on hot paths. This means Motus assemblies are fully compatible with NativeAOT and trimmed deployments without any additional configuration.

### Built-in accessibility testing

`Expect.That(locator)` and `Expect.That(page)` include accessibility-oriented assertions (`ToPassAxeAsync`, `ToHaveAccessibleNameAsync`) without requiring a third-party package. Playwright delegates accessibility auditing to the separate `axe-playwright` ecosystem package.

---

## API Mapping

### Namespaces

| Playwright for .NET | Motus |
|---|---|
| `Microsoft.Playwright` | `Motus.Abstractions` (interfaces) |
| `Microsoft.Playwright` | `Motus` (launcher, options) |
| `Microsoft.Playwright` | `Motus.Assertions` (`Expect`) |
| `Microsoft.Playwright.MSTest` | `Motus.Testing.MSTest` |

### Launch and connect

| Playwright for .NET | Motus |
|---|---|
| `var playwright = await Playwright.CreateAsync()` | (not needed - no sidecar to create) |
| `playwright.Chromium.LaunchAsync(options)` | `MotusLauncher.LaunchAsync(options)` |
| `playwright.Firefox.LaunchAsync(options)` | `MotusLauncher.LaunchAsync(new LaunchOptions { Channel = BrowserChannel.Firefox })` |
| `playwright.Chromium.ConnectAsync(wsEndpoint)` | `MotusLauncher.ConnectAsync(wsEndpoint)` |

### Browser

| Playwright for .NET | Motus |
|---|---|
| `browser.NewContextAsync(options)` | `browser.NewContextAsync(options)` |
| `browser.NewPageAsync()` | `browser.NewPageAsync()` |
| `browser.CloseAsync()` | `browser.CloseAsync()` |
| `browser.IsConnected` | `browser.IsConnected` |
| `browser.Version` | `browser.Version` |
| `browser.Contexts` | `browser.Contexts` |

### Browser context

| Playwright for .NET | Motus |
|---|---|
| `context.NewPageAsync()` | `context.NewPageAsync()` |
| `context.CookiesAsync(urls)` | `context.CookiesAsync(urls)` |
| `context.AddCookiesAsync(cookies)` | `context.AddCookiesAsync(cookies)` |
| `context.ClearCookiesAsync()` | `context.ClearCookiesAsync()` |
| `context.GrantPermissionsAsync(perms, origin)` | `context.GrantPermissionsAsync(perms, origin)` |
| `context.ClearPermissionsAsync()` | `context.ClearPermissionsAsync()` |
| `context.SetGeolocationAsync(geo)` | `context.SetGeolocationAsync(geo)` |
| `context.RouteAsync(pattern, handler)` | `context.RouteAsync(pattern, handler)` |
| `context.UnrouteAsync(pattern, handler)` | `context.UnrouteAsync(pattern, handler)` |
| `context.SetOfflineAsync(offline)` | `context.SetOfflineAsync(offline)` |
| `context.SetExtraHTTPHeadersAsync(headers)` | `context.SetExtraHTTPHeadersAsync(headers)` |
| `context.StorageStateAsync(path)` | `context.StorageStateAsync(path)` |
| `context.ExposeBindingAsync(name, callback)` | `context.ExposeBindingAsync(name, callback)` |
| `context.AddInitScriptAsync(script)` | `context.AddInitScriptAsync(script)` |
| `context.Tracing` | `context.Tracing` |
| (no equivalent) | `context.GetPluginContext()` |

### Page - navigation

| Playwright for .NET | Motus |
|---|---|
| `page.GotoAsync(url, options)` | `page.GotoAsync(url, options)` |
| `page.GoBackAsync(options)` | `page.GoBackAsync(options)` |
| `page.GoForwardAsync(options)` | `page.GoForwardAsync(options)` |
| `page.ReloadAsync(options)` | `page.ReloadAsync(options)` |
| `page.WaitForURLAsync(pattern, options)` | `page.WaitForURLAsync(pattern, options)` |
| `page.WaitForLoadStateAsync(state, timeout)` | `page.WaitForLoadStateAsync(state, timeout)` |
| `page.Url` | `page.Url` |

### Page - locators

| Playwright for .NET | Motus |
|---|---|
| `page.Locator(selector, options)` | `page.Locator(selector, options)` |
| `page.GetByRole(role, new() { Name = name })` | `page.GetByRole(role, name)` |
| `page.GetByText(text, new() { Exact = exact })` | `page.GetByText(text, exact)` |
| `page.GetByLabel(text, new() { Exact = exact })` | `page.GetByLabel(text, exact)` |
| `page.GetByPlaceholder(text, new() { Exact = exact })` | `page.GetByPlaceholder(text, exact)` |
| `page.GetByTestId(testId)` | `page.GetByTestId(testId)` |
| `page.GetByTitle(text, new() { Exact = exact })` | `page.GetByTitle(text, exact)` |
| `page.GetByAltText(text, new() { Exact = exact })` | `page.GetByAltText(text, exact)` |

### Page - evaluation and content

| Playwright for .NET | Motus |
|---|---|
| `page.EvaluateAsync<T>(expression, arg)` | `page.EvaluateAsync<T>(expression, arg)` |
| `page.EvaluateHandleAsync(expression, arg)` | `page.EvaluateHandleAsync(expression, arg)` |
| `page.ContentAsync()` | `page.ContentAsync()` |
| `page.SetContentAsync(html, options)` | `page.SetContentAsync(html, options)` |
| `page.TitleAsync()` | `page.TitleAsync()` |
| `page.ExposeBindingAsync(name, callback)` | `page.ExposeBindingAsync(name, callback)` |

### Page - waiting

| Playwright for .NET | Motus |
|---|---|
| `page.WaitForFunctionAsync(expression, arg, options)` | `page.WaitForFunctionAsync<T>(expression, arg, timeout)` |
| `page.WaitForRequestAsync(pattern, options)` | `page.WaitForRequestAsync(pattern, timeout)` |
| `page.WaitForResponseAsync(pattern, options)` | `page.WaitForResponseAsync(pattern, timeout)` |
| `page.WaitForTimeoutAsync(timeout)` | `page.WaitForTimeoutAsync(timeout)` |

### Page - other

| Playwright for .NET | Motus |
|---|---|
| `page.ScreenshotAsync(options)` | `page.ScreenshotAsync(options)` |
| `page.RouteAsync(pattern, handler)` | `page.RouteAsync(pattern, handler)` |
| `page.UnrouteAsync(pattern, handler)` | `page.UnrouteAsync(pattern, handler)` |
| `page.SetViewportSizeAsync(width, height)` | `page.SetViewportSizeAsync(viewportSize)` |
| `page.AddScriptTagAsync(options)` | `page.AddScriptTagAsync(url, content)` |
| `page.AddStyleTagAsync(options)` | `page.AddStyleTagAsync(url, content)` |
| `page.CloseAsync(new() { RunBeforeUnload = x })` | `page.CloseAsync(runBeforeUnload)` |
| `page.BringToFrontAsync()` | `page.BringToFrontAsync()` |
| `page.PauseAsync()` | `page.PauseAsync()` |
| `page.PdfAsync(options)` | `page.PdfAsync(path)` |
| `page.EmulateMediaAsync(options)` | `page.EmulateMediaAsync(media, colorScheme)` |
| `page.Keyboard` | `page.Keyboard` |
| `page.Mouse` | `page.Mouse` |
| `page.Touchscreen` | `page.Touchscreen` |
| `page.Video` | `page.Video` |
| `page.MainFrame` | `page.MainFrame` |
| `page.Frames` | `page.Frames` |
| `page.IsClosed` | `page.IsClosed` |
| `page.ViewportSize` | `page.ViewportSize` |
| `page.Context` | `page.Context` |

### Locator actions

| Playwright for .NET | Motus |
|---|---|
| `locator.ClickAsync(options)` | `locator.ClickAsync(timeout)` |
| `locator.DblClickAsync(options)` | `locator.DblClickAsync(timeout)` |
| `locator.CheckAsync(options)` | `locator.CheckAsync(timeout)` |
| `locator.UncheckAsync(options)` | `locator.UncheckAsync(timeout)` |
| `locator.SetCheckedAsync(checked, options)` | `locator.SetCheckedAsync(checked, timeout)` |
| `locator.FillAsync(value, options)` | `locator.FillAsync(value, timeout)` |
| `locator.ClearAsync(options)` | `locator.ClearAsync(timeout)` |
| `locator.TypeAsync(text, options)` | `locator.TypeAsync(text, options)` |
| `locator.PressAsync(key, options)` | `locator.PressAsync(key, options)` |
| `locator.FocusAsync(options)` | `locator.FocusAsync(timeout)` |
| `locator.HoverAsync(options)` | `locator.HoverAsync(timeout)` |
| `locator.SelectOptionAsync(values)` | `locator.SelectOptionAsync(values)` |
| `locator.SetInputFilesAsync(files, options)` | `locator.SetInputFilesAsync(files, timeout)` |
| `locator.TapAsync(options)` | `locator.TapAsync(timeout)` |
| `locator.ScrollIntoViewIfNeededAsync(options)` | `locator.ScrollIntoViewIfNeededAsync(timeout)` |
| `locator.ScreenshotAsync(options)` | `locator.ScreenshotAsync(options)` |
| `locator.DispatchEventAsync(type, eventInit)` | `locator.DispatchEventAsync(type, eventInit)` |
| `locator.EvaluateAsync<T>(expression, arg)` | `locator.EvaluateAsync<T>(expression, arg)` |

### Locator queries

| Playwright for .NET | Motus |
|---|---|
| `locator.TextContentAsync(options)` | `locator.TextContentAsync(timeout)` |
| `locator.InnerTextAsync(options)` | `locator.InnerTextAsync(timeout)` |
| `locator.InnerHTMLAsync(options)` | `locator.InnerHTMLAsync(timeout)` |
| `locator.GetAttributeAsync(name, options)` | `locator.GetAttributeAsync(name, timeout)` |
| `locator.InputValueAsync(options)` | `locator.InputValueAsync(timeout)` |
| `locator.BoundingBoxAsync(options)` | `locator.BoundingBoxAsync(timeout)` |
| `locator.CountAsync()` | `locator.CountAsync()` |
| `locator.AllInnerTextsAsync()` | `locator.AllInnerTextsAsync()` |
| `locator.AllTextContentsAsync()` | `locator.AllTextContentsAsync()` |
| `locator.IsCheckedAsync(options)` | `locator.IsCheckedAsync(timeout)` |
| `locator.IsDisabledAsync(options)` | `locator.IsDisabledAsync(timeout)` |
| `locator.IsEditableAsync(options)` | `locator.IsEditableAsync(timeout)` |
| `locator.IsEnabledAsync(options)` | `locator.IsEnabledAsync(timeout)` |
| `locator.IsHiddenAsync(options)` | `locator.IsHiddenAsync(timeout)` |
| `locator.IsVisibleAsync(options)` | `locator.IsVisibleAsync(timeout)` |
| `locator.WaitForAsync(options)` | `locator.WaitForAsync(state, timeout)` |
| `locator.ElementHandleAsync(options)` | `locator.ElementHandleAsync(timeout)` |
| `locator.ElementHandlesAsync()` | `locator.ElementHandlesAsync()` |

### Locator chaining and filtering

| Playwright for .NET | Motus |
|---|---|
| `locator.First` | `locator.First` |
| `locator.Last` | `locator.Last` |
| `locator.Nth(index)` | `locator.Nth(index)` |
| `locator.Filter(options)` | `locator.Filter(options)` |
| `locator.Locator(selector, options)` | `locator.Locator(selector, options)` |

---

## Selector Syntax

Most CSS and text selectors work identically in both frameworks. The differences are in how custom and semantic selectors are spelled.

| Pattern | Playwright for .NET | Motus |
|---|---|---|
| CSS | `"button.primary"` | `"button.primary"` |
| XPath | `"xpath=//button"` | `"xpath=//button"` |
| Text (exact) | `"text=Submit"` | `"text=Submit"` |
| Text (contains) | `"text=ubmi"` | `"text=ubmi"` |
| Role | `page.GetByRole("button", new() { Name = "Submit" })` | `page.GetByRole("button", "Submit")` |
| Test ID (`data-testid`) | `page.GetByTestId("submit-btn")` | `page.GetByTestId("submit-btn")` |
| Label | `page.GetByLabel("Email")` | `page.GetByLabel("Email")` |
| Placeholder | `page.GetByPlaceholder("user@example.com")` | `page.GetByPlaceholder("user@example.com")` |
| Alt text | `page.GetByAltText("Company logo")` | `page.GetByAltText("Company logo")` |
| Title | `page.GetByTitle("Close dialog")` | `page.GetByTitle("Close dialog")` |
| Custom engine | `selectors.RegisterAsync("tag", ...)` (JS-only) | `pluginContext.Register(new MyStrategy())` (.NET) |

---

## Assertion API

This is the most visible syntax difference between the two frameworks.

**Playwright for .NET** uses a static `Expect(subject)` call:

```csharp
await Expect(locator).ToBeVisibleAsync();
await Expect(locator).ToHaveTextAsync("Hello");
await Expect(page).ToHaveTitleAsync("Home");
await Expect(response).ToBeOKAsync();
```

**Motus** uses `Expect.That(subject)`:

```csharp
await Expect.That(locator).ToBeVisibleAsync();
await Expect.That(locator).ToHaveTextAsync("Hello");
await Expect.That(page).ToHaveTitleAsync("Home");
await Expect.That(response).ToBeOKAsync();
```

The assertion methods themselves (`ToBeVisibleAsync`, `ToHaveTextAsync`, `ToBeCheckedAsync`, `ToBeDisabledAsync`, `ToBeEnabledAsync`, `ToBeHiddenAsync`, `ToBeEmptyAsync`, `ToHaveValueAsync`, `ToHaveCountAsync`, `ToHaveAttributeAsync`, `ToHaveClassAsync`, `ToContainTextAsync`, `ToHaveURLAsync`, `ToHaveTitleAsync`, `ToBeOKAsync`) carry identical names and semantics.

The `Expect.That` overloads accept `ILocator`, `IPage`, and `IResponse`, matching the subjects supported by Playwright's `Expect`.

---

## Test Base Class

### Playwright for .NET (`PlaywrightTest`)

```csharp
[TestClass]
public class MyTests : PageTest
{
    [TestMethod]
    public async Task Example()
    {
        await Page.GotoAsync("https://example.com");
        await Expect(Page).ToHaveTitleAsync("Example Domain");
    }
}
```

`PageTest` inherits from `PlaywrightTest` and injects `Page`, `Context`, and `Browser` properties. The framework creates a new browser for every test class by default, and a new context and page per test.

### Motus (`MotusTestBase`)

```csharp
[AssemblyInitialize]
public static async Task AssemblyInit(TestContext _)
    => await MotusTestBase.LaunchBrowserAsync();

[AssemblyCleanup]
public static async Task AssemblyCleanup()
    => await MotusTestBase.CloseBrowserAsync();

[TestClass]
public class MyTests : MotusTestBase
{
    [TestMethod]
    public async Task Example()
    {
        await Page.GotoAsync("https://example.com");
        await Expect.That(Page).ToHaveTitleAsync("Example Domain");
    }
}
```

The key behavioral difference is that Motus shares a **single browser process** across the entire test assembly. Each test gets an isolated `IBrowserContext` and `IPage` (created in `[TestInitialize]`, torn down in `[TestCleanup]`). This is faster than Playwright's default one-browser-per-class model because browser startup happens only once.

`MotusTestBase` exposes the following virtual members for customization:

| Member | Purpose |
|---|---|
| `LaunchOptions? LaunchOptions` | Override browser launch settings for the assembly |
| `ContextOptions? ContextOptions` | Override per-test context settings (default viewport is 1024x768) |

The `[Parallelize]` MSTest attribute is supported. Each parallel test worker receives its own context and page.

---

## Configuration

### Playwright for .NET

Playwright configuration lives in a `playwright.config.ts` TypeScript file processed by the Playwright CLI toolchain:

```typescript
import { defineConfig } from '@playwright/test';
export default defineConfig({
  timeout: 30000,
  use: {
    baseURL: 'http://localhost:5000',
    headless: true,
    viewport: { width: 1280, height: 720 },
  },
});
```

### Motus

Motus reads an optional `motus.config.json` file from the working directory (or the path set in the `MOTUS_CONFIG` environment variable). The same values can also be supplied programmatically via `LaunchOptions` and `ContextOptions`.

```json
{
  "headless": true,
  "channel": "chrome",
  "slowMo": 0,
  "timeout": 30000,
  "viewport": { "width": 1280, "height": 720 },
  "baseURL": "http://localhost:5000",
  "tracing": {
    "onFailure": true
  }
}
```

All keys in `motus.config.json` correspond directly to property names on `LaunchOptions` and `ContextOptions`. There is no CLI toolchain; configuration is consumed at runtime by `MotusLauncher.LaunchAsync`, which merges file-based settings with any programmatic overrides (`ConfigMerge.ApplyConfig`).

Environment variables prefixed with `MOTUS_` override individual config values and take the highest precedence (for example, `MOTUS_HEADLESS=false`).

---

## Step-by-Step Migration Guide

### 1. Replace the NuGet package

Remove `Microsoft.Playwright` (and `Microsoft.Playwright.MSTest` if present). Add the Motus packages:

```xml
<PackageReference Include="Motus" Version="..." />
<PackageReference Include="Motus.Testing.MSTest" Version="..." />
```

### 2. Remove the Playwright CLI install step

Delete any `playwright install` or `pwsh bin/Debug/.../playwright.ps1 install` commands from your build scripts. Motus discovers the system browser or a browser installed via the Motus browser installer; there is no generated script to run.

### 3. Update using directives

Replace:

```csharp
using Microsoft.Playwright;
using Microsoft.Playwright.MSTest;
```

With:

```csharp
using Motus;
using Motus.Abstractions;
using Motus.Assertions;
using Motus.Testing.MSTest;
```

### 4. Remove `Playwright.CreateAsync()`

Delete any code that calls `Playwright.CreateAsync()` and stores the `IPlaywright` instance. In Motus there is no intermediate object between startup and the browser. The equivalent launch call is:

```csharp
// Before
var playwright = await Playwright.CreateAsync();
var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });

// After
var browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
```

### 5. Update assertion calls

Do a project-wide search-and-replace:

| Find | Replace |
|---|---|
| `Expect(` | `Expect.That(` |

The assertion method names are unchanged so no further edits are needed on assertion lines.

### 6. Update test base class

Replace `PageTest` (or `PlaywrightTest`) inheritance with `MotusTestBase`:

```csharp
// Before
[TestClass]
public class MyTests : PageTest { ... }

// After
[TestClass]
public class MyTests : MotusTestBase { ... }
```

Add `[AssemblyInitialize]` and `[AssemblyCleanup]` hooks to a static setup class if you do not already have them (see the example in the Test Base Class section above).

### 7. Update `GetByRole` calls

Playwright's `GetByRole` accepts an options object for the accessible name. Motus accepts it as a plain second argument:

```csharp
// Before
page.GetByRole(AriaRole.Button, new() { Name = "Submit" })

// After
page.GetByRole("button", "Submit")
```

### 8. Migrate configuration

If you relied on `playwright.config.ts` for default timeout, viewport, or base URL, create a `motus.config.json` in your project output directory with the equivalent values (see the Configuration section above). Remove any Playwright config file from the project.

### 9. Remove `[assembly: Parallelize]` workarounds

If you previously limited parallelism to work around Playwright's per-class browser overhead, you can remove those restrictions. Because Motus reuses a single browser process, parallel context creation is cheap.

### 10. Run and triage

Build and run your test suite. The most common remaining failures after the steps above are:

- Options objects that were inline anonymous types in Playwright but are now named `ContextOptions`, `LaunchOptions`, `NavigationOptions`, or `LocatorOptions` in Motus.
- `AriaRole` enum values (Playwright) replaced by plain role-name strings in Motus (`GetByRole("button", ...)`).
- `page.SetViewportSizeAsync` now takes a `ViewportSize` record rather than two `int` parameters.

---

## See Also

- [MotusTestBase API reference](../guides/testing-mstest.md)
- [Plugin extensibility](../extensions/overview.md)
- [Tracing and failure capture](../guides/tracing.md)
- [Configuration reference](../guides/configuration.md)
- [Accessibility assertions](../guides/accessibility.md)
