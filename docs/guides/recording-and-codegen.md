# Recording and Code Generation

Motus ships two CLI commands that eliminate the need to write browser test boilerplate by hand. `motus record` watches your interactions with a live browser and emits a ready-to-compile C# test file. `motus codegen` inspects one or more pages and generates strongly-typed Page Object Model (POM) classes. Both commands are part of `Motus.Cli`.

---

## motus record

`motus record` opens a headed browser, injects a lightweight recorder script into every page frame, and converts your clicks, keystrokes, and navigation into C# test code. When you press **Enter** in the terminal the recording stops, actions are coalesced, and the output file is written.

### How it works

1. **Browser launch.** A headed browser instance is launched (or an existing one is attached to via `--connect`). A new page is opened at the configured viewport size (default 1024x768, adjustable with `--width` and `--height`).
2. **Script injection.** The `ActionCaptureEngine` registers a DOM binding and injects the recorder script via `AddInitScript` so that the listener survives page navigations automatically. The script is also evaluated immediately on the current page if one is already loaded.
3. **Event capture.** The `InputStateMachine` receives raw DOM events (mouse, keyboard, form changes) through the binding callback and the CDP session. Navigation events are captured via the `FrameNavigated` page event. Dialog open/close state is tracked across the `Dialog` page event and the `Page.javascriptDialogClosed` CDP event. File chooser activations are captured via the `FileChooser` page event.
4. **Selector inference.** For actions that target a specific element (click, fill, select, check, file upload), the `SelectorInferenceEngine` resolves the element and walks registered selector strategies in priority order until it finds a selector that matches exactly one node in the DOM.
5. **Code emission.** After `StopAsync` drains the inference pipeline, `CodeEmitter` coalesces consecutive fill actions on the same selector (to handle CDP latency) and emits one line of C# per action using the chosen framework template.

### CLI options

| Option | Default | Description |
|---|---|---|
| `--url` | _(none)_ | Starting URL to navigate to before recording begins |
| `--output` | `recorded-test.cs` | Output file path for the generated test code |
| `--framework` | `mstest` | Test framework (`mstest`, `xunit`, `nunit`) |
| `--connect` | _(none)_ | WebSocket endpoint of an existing browser (e.g. `ws://localhost:9222`) |
| `--selector-priority` | _(none)_ | Selector priority strategy (reserved for future use) |
| `--class-name` | `RecordedTest` | Name of the generated test class |
| `--method-name` | `RecordedScenario` | Name of the generated test method |
| `--namespace` | `Motus.Generated` | Namespace for the generated file |
| `--preserve-timing` | `false` | Emit `Task.Delay` calls between actions matching the original timing gaps (gaps shorter than 250 ms are omitted) |
| `--width` | `1024` | Viewport width in pixels |
| `--height` | `768` | Viewport height in pixels |

When `--connect` is not provided, the command always launches a headed browser regardless of any other flags. There is no `--headless` option for `motus record`.

### Basic example

```bash
motus record --url https://example.com/login --output tests/LoginTest.cs --framework xunit --class-name LoginTest --method-name CanSignIn
```

---

## Action capture

The following action types are captured during a recording session:

| Action | Trigger | Emitted as |
|---|---|---|
| **Click** | `mousedown` / `click` DOM event | `page.Locator(selector).ClickAsync()` |
| **Fill** | `input` / `change` DOM event on text inputs and textareas | `page.Locator(selector).FillAsync(value)` |
| **Select** | `change` on `<select>` elements | `page.Locator(selector).SelectOptionAsync(value)` |
| **Check / Uncheck** | `change` on checkboxes and radio buttons | `page.Locator(selector).CheckAsync()` or `UncheckAsync()` |
| **Navigate** | `FrameNavigated` page event | `page.GotoAsync(url)` |
| **Dialog** | `Dialog` page event + `Page.javascriptDialogClosed` CDP event | `page.Dialog += (_, d) => d.AcceptAsync()` or `d.DismissAsync()` |
| **File upload** | `FileChooser` page event | `page.Locator(selector).SetInputFilesAsync(...)` |
| **Scroll** | `wheel` DOM event | `page.Mouse.MoveAsync(x, y)` + `page.Mouse.WheelAsync(dx, dy)` |
| **Keyboard** | Standalone key press events | `page.Keyboard.PressAsync(key)` |

When selector inference fails for an action that requires a selector, the emitter writes a `// TODO:` comment with the coordinates and value so you can supply the selector manually.

---

## Selector inference

The `SelectorInferenceEngine` determines the best unique selector for each captured element. The process runs as an async pipeline that drains between the raw event channel and the resolved action channel.

### Resolution order

1. If the recorder script stored a reference to the target element at event time (via `__motus_get_target__`), that handle is retrieved directly. This survives DOM mutations that occur between the event and inference.
2. If no stored reference is available, the element is located by coordinate hit-testing using `DOM.getNodeForLocation` over CDP.

### Strategy priority

Once the element handle is obtained, each registered `ISelectorStrategy` is tried in priority order. The first strategy that produces a selector satisfying both of these conditions is accepted:

- The selector is no longer than the configured maximum length (default 200 characters).
- Resolving the selector against the page's main frame (with shadow DOM piercing) returns exactly one match.

If no strategy produces a unique selector within the configured inference timeout, the selector is recorded as `null` and a `// TODO:` comment is emitted in the output file.

The default strategy order can be overridden at the `PageAnalysisOptions` level; the `--selector-priority` flag in the CLI is reserved for a future release.

---

## motus codegen

`motus codegen` analyzes one or more live web pages and generates a `.g.cs` partial class for each page. Each class includes typed `ILocator` properties for discovered elements, a constructor, a `NavigateAsync` method, and auto-generated form action helpers for forms that contain a submit button.

### How it works

1. A browser is launched (or connected to) based on the provided options.
2. For each URL the page is loaded and `WaitForLoadStateAsync(NetworkIdle)` is called before analysis begins.
3. `PageAnalysisEngine` crawls the DOM, discovers interactive elements, and runs selector inference on each one.
4. `PomEmitter` converts the list of `DiscoveredElement` records into a partial C# class and writes it to `<ClassName>.g.cs` in the output directory.
5. Class names are derived from the page URL by `PageClassNameDeriver`.

### CLI options

| Option | Default | Description |
|---|---|---|
| `url` (argument) | _(none)_ | One or more URLs to analyze (zero or more; optional with `--connect` or `--headed`) |
| `--output` | `.` | Output directory for generated files |
| `--namespace` | `Motus.Generated` | Namespace for generated classes |
| `--selector-priority` | _(none)_ | Comma-separated strategy order, e.g. `testid,role,text,css` |
| `--timeout` | `30000` | Navigation timeout in milliseconds |
| `--detect-listeners` | `false` | Run a second analysis pass using `DOMDebugger.getEventListeners` to discover elements with directly-attached JS handlers (vanilla JS, jQuery). React event delegation is not captured. |
| `--connect` | _(none)_ | WebSocket endpoint of an existing browser. When provided and no URLs are given, the currently active page is analyzed. |
| `--headed` | `false` | Launch a visible browser so you can navigate interactively. The CLI prompts you to press Enter when you are ready for analysis. |
| `--scope` | _(none)_ | CSS selector to limit element discovery to a container, e.g. `"#login-form"` or `".modal-dialog"` |

At least one of: a URL argument, `--connect`, or `--headed` must be provided.

When `--headed` is used without URLs, the CLI opens a blank page and waits for you to navigate before prompting for analysis. When `--headed` is combined with URLs, it navigates to each URL and pauses for interaction before analyzing.

When `--connect` is used without URLs, the engine analyzes whichever page tab was most recently active in the connected browser.

### Generated output

Each generated file is a partial class that you extend with your own assertion and helper methods:

```csharp
// <auto-generated/>
using Motus.Abstractions;

namespace Motus.Generated;

public partial class LoginPage
{
    private readonly IPage _page;

    public LoginPage(IPage page) => _page = page;

    public Task NavigateAsync() => _page.GotoAsync("https://example.com/login");

    // Locators
    public ILocator UsernameInput => _page.Locator("#username");
    public ILocator PasswordInput => _page.Locator("#password");
    public ILocator SignInButton  => _page.Locator("[data-testid='sign-in']");

    // Form actions
    public async Task SubmitSignInFormAsync(string username, string password)
    {
        await UsernameInput.FillAsync(username);
        await PasswordInput.FillAsync(password);
        await SignInButton.ClickAsync();
    }
}
```

Elements for which no unique selector could be inferred are emitted as `// TODO:` comments so you can fill them in manually.

---

## Framework output options

`motus record` supports three test frameworks via `--framework`. The generated boilerplate differs per framework but the body of the test method is identical.

### MSTest (default)

```csharp
using Motus.Abstractions;
using Motus.Testing.MSTest;

namespace Motus.Generated;

[TestClass]
public class RecordedTest : MotusTestBase
{
    [TestMethod]
    public async Task RecordedScenario()
    {
        var page = Page;

        // recorded actions...
    }
}
```

### xUnit

```csharp
using Motus.Abstractions;
using Motus.Testing.xUnit;

namespace Motus.Generated;

[Collection(nameof(MotusCollection))]
public class RecordedTest : IAsyncLifetime
{
    private readonly BrowserContextFixture _fixture;
    private IPage _page = null!;

    public RecordedTest(BrowserContextFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _page = _fixture.Page;
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Task.CompletedTask;
    }

    [Fact]
    public async Task RecordedScenario()
    {
        var page = _page;

        // recorded actions...
    }
}
```

### NUnit

```csharp
using Motus.Abstractions;
using Motus.Testing.NUnit;

namespace Motus.Generated;

[TestFixture]
public class RecordedTest : MotusTestBase
{
    [Test]
    public async Task RecordedScenario()
    {
        var page = Page;

        // recorded actions...
    }
}
```

---

## Practical workflow

The typical workflow when building a new test suite from scratch:

**Step 1 - Generate page objects for your application.**

```bash
motus codegen https://myapp.local/login https://myapp.local/dashboard \
    --output tests/Pages \
    --namespace MyApp.Tests.Pages \
    --selector-priority testid,role,css
```

This writes `LoginPage.g.cs` and `DashboardPage.g.cs` to `tests/Pages/`.

**Step 2 - Record a scenario.**

```bash
motus record --url https://myapp.local/login \
    --output tests/LoginFlow.cs \
    --framework xunit \
    --namespace MyApp.Tests \
    --class-name LoginFlowTests \
    --method-name CanLoginAndReachDashboard
```

Interact with the browser. When done, press **Enter**. The recorded steps are written to `tests/LoginFlow.cs`.

**Step 3 - Refactor the recorded test to use your page objects.**

Replace the raw `page.Locator(...)` calls in the generated test body with calls to the typed page object methods you generated in step 1:

```csharp
var loginPage = new LoginPage(page);
await loginPage.NavigateAsync();
await loginPage.SubmitSignInFormAsync("alice@example.com", "hunter2");
```

**Step 4 - Connect to a running application for iterative codegen.**

```bash
# Start Chrome with remote debugging enabled
google-chrome --remote-debugging-port=9222

# Analyze whatever page is currently open
motus codegen --connect ws://localhost:9222 --output tests/Pages --namespace MyApp.Tests.Pages
```

---

## See also

- [Configuration](configuration.md) - browser launch options and timeouts
- [Testing Frameworks](testing-frameworks.md) - `MotusTestBase`, fixtures, and collection setup
- [Network Interception](network-interception.md) - intercept and mock requests during recorded playback
