# Migrating from Selenium WebDriver

This guide helps you transition an existing Selenium WebDriver test suite to Motus. The two frameworks share the same goal (automated browser control for testing) but differ substantially in architecture, element interaction model, and async design. Most migrations can be completed mechanically for common patterns, with a small number of conceptual shifts required for waiting and assertions.

---

## Conceptual Differences

### Transport protocol

Selenium WebDriver communicates with the browser through the W3C WebDriver HTTP protocol. Every command is an HTTP round trip to a driver binary (chromedriver, geckodriver) that you must download, version-pin, and keep in sync with the installed browser.

Motus communicates directly with the browser over a persistent WebSocket connection using Chrome DevTools Protocol (CDP) for Chromium-based browsers and WebDriver BiDi for Firefox. There is no driver binary to manage. `MotusLauncher.LaunchAsync()` starts the browser process itself, negotiates the connection, and returns an `IBrowser` ready for use.

### Element references: IWebElement vs ILocator

Selenium's `IWebElement` is an eager reference. Calling `driver.FindElement()` queries the DOM immediately and returns a handle to whichever DOM node matched at that instant. If the page re-renders after the call, the handle becomes stale and the next operation throws `StaleElementReferenceException`.

Motus's `ILocator` is a lazy description of how to find an element. Calling `page.Locator()` records a selector but performs no DOM query. The query runs at the moment of each action (`ClickAsync`, `FillAsync`, etc.), so the locator always resolves against the current DOM. Locators never go stale.

### Waiting

Selenium requires explicit coordination with the DOM through `WebDriverWait`, implicit waits, or `ExpectedConditions`. Forgetting a wait is a common source of flaky tests.

Motus builds actionability checks into every action. Before performing a click, fill, or other interaction, Motus waits for the target element to be attached, visible, stable (not animating), and enabled. This happens automatically on every call with a configurable timeout. Most `WebDriverWait` constructs are unnecessary in Motus.

### PageFactory

Selenium's PageFactory pattern was introduced to manage the cost of repeated `FindElement` calls and to group locators into page objects. Because Motus locators are lightweight descriptions with no DOM cost at construction time, there is no equivalent pattern needed. Locators are composable: a child locator can be scoped from a parent with `locator.Locator(selector)`, and collections can be filtered with `.First`, `.Last`, `.Nth(n)`, and `.Filter()`.

### Async-first API

Selenium's API is synchronous. Motus's API is fully async. Every action, query, and navigation returns `Task` or `Task<T>` and must be awaited. Test methods should be declared `async Task`.

---

## Selector Migration

| Selenium `By` | Motus equivalent |
|---|---|
| `By.Id("x")` | `page.Locator("#x")` |
| `By.ClassName("x")` | `page.Locator(".x")` |
| `By.CssSelector("x")` | `page.Locator("x")` |
| `By.XPath("//x")` | `page.Locator("xpath=//x")` |
| `By.Name("x")` | `page.Locator("[name='x']")` |
| `By.LinkText("x")` | `page.GetByRole("link", "x")` |
| `By.TagName("x")` | `page.Locator("x")` |
| (no equivalent) | `page.GetByRole(role, name)` |
| (no equivalent) | `page.GetByText(text)` |
| (no equivalent) | `page.GetByLabel(text)` |
| (no equivalent) | `page.GetByPlaceholder(text)` |
| (no equivalent) | `page.GetByTestId(testId)` |
| (no equivalent) | `page.GetByTitle(text)` |
| (no equivalent) | `page.GetByAltText(text)` |

Prefer semantic locators (`GetByRole`, `GetByLabel`, `GetByText`) over CSS or XPath where possible. Semantic locators are resilient to markup changes and double as accessibility checks.

---

## Common Operations

| Selenium | Motus |
|---|---|
| `driver.FindElement(by)` | `page.Locator(selector)` (lazy) |
| `driver.FindElement(by)` (eager handle) | `await locator.ElementHandleAsync()` |
| `element.Click()` | `await locator.ClickAsync()` |
| `element.SendKeys("text")` | `await locator.FillAsync("text")` |
| `element.Clear()` | `await locator.ClearAsync()` |
| `element.Text` | `await locator.TextContentAsync()` |
| `element.GetAttribute("name")` | `await locator.GetAttributeAsync("name")` |
| `element.GetDomProperty("value")` | `await locator.InputValueAsync()` |
| `element.Displayed` | `await locator.IsVisibleAsync()` |
| `element.Enabled` | `await locator.IsEnabledAsync()` |
| `element.Selected` | `await locator.IsCheckedAsync()` |
| `new SelectElement(element).SelectByValue("v")` | `await locator.SelectOptionAsync("v")` |
| `element.SendKeys(Keys.Enter)` | `await locator.PressAsync("Enter")` |
| `driver.Navigate().GoToUrl(url)` | `await page.GotoAsync(url)` |
| `driver.Navigate().Back()` | `await page.GoBackAsync()` |
| `driver.Navigate().Forward()` | `await page.GoForwardAsync()` |
| `driver.Navigate().Refresh()` | `await page.ReloadAsync()` |
| `driver.Title` | `await page.TitleAsync()` |
| `driver.Url` | `page.Url` |
| `driver.PageSource` | `await page.ContentAsync()` |
| `(IJavaScriptExecutor)driver).ExecuteScript(expr)` | `await page.EvaluateAsync<T>(expr)` |
| `driver.GetScreenshot()` | `await page.ScreenshotAsync()` |
| `new WebDriverWait(driver, timeout)` | Built-in auto-wait (see below) |
| `Assert.AreEqual(expected, element.Text)` | `await Expect.That(locator).ToHaveTextAsync(expected)` |

---

## Waiting

### Selenium explicit waits

```csharp
// Selenium: explicit wait required before every interaction
var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
var element = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("submit")));
element.Click();
```

### Motus auto-wait

```csharp
// Motus: actionability is checked automatically
await page.Locator("#submit").ClickAsync();
```

Every action in Motus polls until the element satisfies all actionability conditions or the timeout is reached. The default timeout is 30 seconds and can be overridden per-call:

```csharp
await page.Locator("#submit").ClickAsync(timeout: 5000);
```

### When explicit waiting is still needed

Motus provides explicit waiting utilities for cases that actionability checks do not cover:

| Scenario | Motus API |
|---|---|
| Wait for a specific element state | `await locator.WaitForAsync(ElementState.Visible)` |
| Wait for navigation to complete | `await page.WaitForLoadStateAsync(LoadState.NetworkIdle)` |
| Wait for URL to match a pattern | `await page.WaitForURLAsync("**/dashboard")` |
| Wait for a network request | `await page.WaitForRequestAsync(urlPattern)` |
| Wait for a network response | `await page.WaitForResponseAsync(urlPattern)` |
| Wait for a JavaScript condition | `await page.WaitForFunctionAsync<bool>("() => window.ready === true")` |

---

## Assertions

Motus provides a dedicated assertion API in the `Motus.Assertions` namespace that retries until the condition is met or the timeout expires, which eliminates timing-related assertion failures.

```csharp
using Motus.Assertions;

// Selenium
Assert.AreEqual("Welcome", driver.FindElement(By.Id("heading")).Text);

// Motus
await Expect.That(page.Locator("#heading")).ToHaveTextAsync("Welcome");
```

Available locator assertions:

| Assertion | Description |
|---|---|
| `ToBeVisibleAsync()` | Element is visible |
| `ToBeHiddenAsync()` | Element is hidden |
| `ToBeEnabledAsync()` | Element is enabled |
| `ToBeDisabledAsync()` | Element is disabled |
| `ToBeCheckedAsync()` | Checkbox or radio is checked |
| `ToBeEditableAsync()` | Element is editable |
| `ToBeEmptyAsync()` | Input is empty |
| `ToBeAttachedAsync()` | Element is in the DOM |
| `ToBeDetachedAsync()` | Element is not in the DOM |
| `ToHaveTextAsync(text)` | Text content equals the expected value |
| `ToContainTextAsync(text)` | Text content contains the expected value |
| `ToHaveValueAsync(value)` | Input value equals the expected value |
| `ToHaveAttributeAsync(name, value)` | Attribute equals the expected value |
| `ToHaveClassAsync(className)` | Element has the specified CSS class |
| `ToHaveCSSAsync(property, value)` | Computed CSS property equals the expected value |
| `ToHaveCountAsync(n)` | Locator matches exactly n elements |
| `ToHaveAccessibleNameAsync(name)` | Accessible name equals the expected value |
| `ToHaveRoleAsync(role)` | Accessible role equals the expected value |

Assertions can be negated with `.Not`:

```csharp
await Expect.That(page.Locator("#spinner")).Not.ToBeVisibleAsync();
```

Page-level assertions are also available:

```csharp
await Expect.That(page).ToHaveUrlAsync("**/confirmation");
await Expect.That(page).ToHaveTitleAsync("Order Complete");
```

---

## Configuration

### Selenium driver options

```csharp
var options = new ChromeOptions();
options.AddArgument("--headless");
options.AddArgument("--window-size=1280,800");
var driver = new ChromeDriver(options);
```

### Motus LaunchOptions and ContextOptions

Browser launch behavior is configured through `LaunchOptions`. Per-test context settings such as viewport, locale, and credentials are configured through `ContextOptions`.

```csharp
var browser = await MotusLauncher.LaunchAsync(new LaunchOptions
{
    Headless = true,
    Channel = BrowserChannel.Chrome,
    SlowMo = 50,           // milliseconds between actions, useful for debugging
    Args = ["--disable-gpu"]
});

var context = await browser.NewContextAsync(new ContextOptions
{
    Viewport = new ViewportSize(1280, 800),
    Locale = "en-US",
    TimezoneId = "America/New_York",
    BaseURL = "https://localhost:5001",
    IgnoreHTTPSErrors = true
});

var page = await context.NewPageAsync();
```

`LaunchOptions` key properties:

| Property | Default | Description |
|---|---|---|
| `Headless` | `true` | Run browser without a visible window |
| `Channel` | null | Browser channel (`Chrome`, `Firefox`, etc.) |
| `ExecutablePath` | null | Path to a custom browser binary |
| `SlowMo` | `0` | Delay in milliseconds added after each action |
| `Timeout` | `30000` | Maximum milliseconds to wait for browser startup |
| `Args` | null | Additional command-line arguments |
| `UserDataDir` | null | Persistent profile directory; temporary profile used if null |

`ContextOptions` key properties:

| Property | Default | Description |
|---|---|---|
| `Viewport` | null | Viewport dimensions |
| `BaseURL` | null | Base URL for relative navigations |
| `Locale` | null | Browser locale |
| `TimezoneId` | null | Timezone identifier |
| `UserAgent` | null | Custom user-agent string |
| `IgnoreHTTPSErrors` | `false` | Ignore TLS certificate errors |
| `HttpCredentials` | null | Credentials for HTTP authentication |
| `StorageState` | null | Pre-loaded cookies and local storage |

---

## Step-by-Step Migration Guide

**1. Remove WebDriver NuGet packages.**

Remove `Selenium.WebDriver`, `Selenium.WebDriver.ChromeDriver`, and any related packages. Add `Motus` and, if using MSTest, `Motus.Testing.MSTest`.

**2. Replace driver initialization.**

Replace `new ChromeDriver()` / `new FirefoxDriver()` with `await MotusLauncher.LaunchAsync()`. The returned `IBrowser` is the top-level object analogous to `IWebDriver`.

**3. Create a context and page.**

Call `await browser.NewContextAsync()` to get an `IBrowserContext`, then `await context.NewPageAsync()` for an `IPage`. If you only need a single context, the shorthand `await browser.NewPageAsync()` handles both steps.

**4. Adopt MotusTestBase for MSTest projects.**

Extend `MotusTestBase` instead of managing setup and teardown manually. The base class shares a single browser across the assembly and creates an isolated context per test, which is compatible with `[Parallelize]`:

```csharp
[TestClass]
public class MyTests : MotusTestBase
{
    [AssemblyInitialize]
    public static async Task AssemblyInit(TestContext _)
        => await LaunchBrowserAsync();

    [AssemblyCleanup]
    public static async Task AssemblyCleanup()
        => await CloseBrowserAsync();

    [TestMethod]
    public async Task ShouldDisplayWelcomeMessage()
    {
        await Page.GotoAsync("/home");
        await Expect.That(Page.GetByRole("heading", "Welcome")).ToBeVisibleAsync();
    }
}
```

**5. Replace `FindElement` calls with locators.**

Replace every `driver.FindElement(By.X(...))` call with `page.Locator(selector)` or a semantic `GetBy*` method. Do not store locators in fields expecting them to reflect live DOM state; that is already their default behavior.

**6. Await all actions.**

Add `await` to every element interaction and navigation call. Mark test methods `async Task`.

**7. Remove explicit waits.**

Delete `WebDriverWait`, `Thread.Sleep`, and `ImplicitlyWait` calls. Motus auto-wait handles the common cases. For the uncommon cases, use the `WaitFor*Async` methods listed in the waiting section above.

**8. Replace assertions.**

Replace `Assert.AreEqual(expected, element.Text)` and similar patterns with `Expect.That(locator).ToHave*Async()` assertions, which retry internally and produce clearer failure messages.

**9. Dispose correctly.**

`IBrowser`, `IBrowserContext`, and `IPage` are all `IAsyncDisposable`. Use `await using` declarations or call `CloseAsync()` / `DisposeAsync()` in test cleanup. `MotusTestBase` handles this automatically when used.

---

## See Also

- `IPage` interface: `src/Motus.Abstractions/IPage.cs`
- `ILocator` interface: `src/Motus.Abstractions/ILocator.cs`
- `IBrowser` interface: `src/Motus.Abstractions/IBrowser.cs`
- `MotusLauncher`: `src/Motus/Motus.cs`
- `Expect` assertions: `src/Motus/Assertions/Expect.cs`
- `MotusTestBase` for MSTest: `src/Motus.Testing.MSTest/MotusTestBase.cs`
- `LaunchOptions`: `src/Motus.Abstractions/Options/LaunchOptions.cs`
- `ContextOptions`: `src/Motus.Abstractions/Options/ContextOptions.cs`
