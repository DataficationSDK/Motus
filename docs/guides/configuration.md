# Configuration

Motus resolves configuration from three layers, applied in order of increasing precedence:

1. **`motus.config.json`** -- a project-level JSON file discovered by walking up the directory tree.
2. **Environment variables** -- `MOTUS_*` variables that override individual file settings.
3. **Code** -- `LaunchOptions` and `ContextOptions` values passed directly at call sites always win.

Each layer is additive. A value set in a higher-precedence layer is never overwritten by a lower one. If no configuration is present at any layer, Motus starts with built-in defaults and never throws.

---

## motus.config.json

### File Discovery

When Motus initializes, it walks up from `Environment.CurrentDirectory`, checking each directory for a `motus.config.json` file. The first file found is used. If no file is found anywhere in the directory tree, Motus proceeds with defaults. Discovery is performed once at startup and the result is cached for the lifetime of the process.

### Complete Schema

```json
{
  "motus": "1.0",
  "launch": {
    "headless": true,
    "channel": "Chrome",
    "slowMo": 0,
    "timeout": 30000
  },
  "context": {
    "locale": "en-US",
    "colorScheme": "Light",
    "recordVideo": false,
    "ignoreHTTPSErrors": false,
    "viewport": {
      "width": 1280,
      "height": 720
    }
  },
  "locator": {
    "timeout": 5000,
    "selectorPriority": ["role", "label", "placeholder", "text", "testid", "css"]
  },
  "assertions": {
    "timeout": 5000
  },
  "reporter": {
    "default": ["console", "html"],
    "ci": ["junit", "html"]
  },
  "recorder": {
    "output": "tests/recorded",
    "framework": "motus",
    "selectorPriority": ["role", "label", "testid", "css"]
  },
  "failure": {
    "screenshot": true,
    "screenshotPath": "test-results/failures",
    "trace": false,
    "tracePath": "test-results/traces"
  },
  "accessibility": {
    "enable": false,
    "mode": "Enforce",
    "auditAfterNavigation": true,
    "auditAfterActions": false,
    "includeWarnings": true,
    "skipRules": []
  }
}
```

### `launch` Section

Controls how the browser process is started.

| Property | Type | Default | Description |
|---|---|---|---|
| `headless` | `bool` | `true` | Run the browser without a visible window. |
| `channel` | `string` | `null` | Browser channel to use. Accepted values: `Chrome`, `Edge`, `Chromium`, `Firefox`. Case-insensitive. |
| `slowMo` | `int` (ms) | `0` | Add a fixed delay after every browser operation. Useful for visual debugging. |
| `timeout` | `int` (ms) | `30000` | Maximum time to wait for the browser process to start. |

### `context` Section

Controls the default browser context created for each test.

| Property | Type | Default | Description |
|---|---|---|---|
| `locale` | `string` | `null` | BCP 47 locale tag applied to the context, e.g. `"en-US"` or `"fr-FR"`. |
| `colorScheme` | `string` | `null` | Preferred color scheme emulated. Accepted values: `Light`, `Dark`, `NoPreference`. Case-insensitive. |
| `recordVideo` | `bool` | `false` | Whether to record video for all pages in the context. Configuring full video output requires `RecordVideoOptions` in code. |
| `ignoreHTTPSErrors` | `bool` | `false` | Ignore TLS certificate errors on all requests. |
| `viewport.width` | `int` | `null` | Viewport width in pixels. Both `width` and `height` must be set for the viewport to apply. |
| `viewport.height` | `int` | `null` | Viewport height in pixels. Both `width` and `height` must be set for the viewport to apply. |

### `locator` Section

Controls the default behavior of element locators.

| Property | Type | Default | Description |
|---|---|---|---|
| `timeout` | `int` (ms) | `null` | Maximum time to wait for a locator to find its element. |
| `selectorPriority` | `string[]` | `null` | Ordered list of selector strategies the locator engine tries before falling back to CSS. |

### `assertions` Section

Controls the default timeout used by Motus assertion helpers.

| Property | Type | Default | Description |
|---|---|---|---|
| `timeout` | `int` (ms) | `null` | Maximum time to wait for an assertion condition to become true. |

### `reporter` Section

Selects which reporters are active. Reporters are referenced by their registered plugin keys.

| Property | Type | Default | Description |
|---|---|---|---|
| `default` | `string[]` | `null` | Reporters used in non-CI runs. |
| `ci` | `string[]` | `null` | Reporters used when a CI environment is detected. When set, this list replaces `default` in CI. |

### `recorder` Section

Controls code generation when using the Motus Recorder.

| Property | Type | Default | Description |
|---|---|---|---|
| `output` | `string` | `null` | Directory where recorded test files are written. |
| `framework` | `string` | `null` | Target framework for generated code, e.g. `"motus"`. |
| `selectorPriority` | `string[]` | `null` | Ordered list of selector strategies the recorder prefers when generating locators. |

### `failure` Section

Controls automatic diagnostics captured when a test fails.

| Property | Type | Default | Description |
|---|---|---|---|
| `screenshot` | `bool` | `null` | Take a screenshot on test failure. |
| `screenshotPath` | `string` | `null` | Directory to write failure screenshots. |
| `trace` | `bool` | `null` | Capture a Playwright-compatible trace archive on test failure. |
| `tracePath` | `string` | `null` | Directory to write trace archives. |

### `accessibility` Section

Controls the built-in accessibility audit hook. When enabled, Motus runs axe-style audits automatically at the points configured below.

| Property | Type | Default | Description |
|---|---|---|---|
| `enable` | `bool` | `false` | Master switch for the accessibility audit hook. |
| `mode` | `string` | `"Enforce"` | How violations are handled. Accepted values: `Off`, `Warn`, `Enforce`. `Enforce` fails the test on error-severity violations. `Warn` logs them without failing. `Off` disables auditing regardless of `enable`. |
| `auditAfterNavigation` | `bool` | `true` | Run an audit after each page navigation. |
| `auditAfterActions` | `bool` | `false` | Run an audit after mutating actions such as click, fill, and selectOption. |
| `includeWarnings` | `bool` | `true` | Treat warning-severity findings as failures alongside errors (when mode is `Enforce`). |
| `skipRules` | `string[]` | `null` | Rule IDs to exclude from every audit, e.g. `["a11y-color-contrast"]`. |

---

## Environment Variables

Environment variables apply after the config file is parsed and override any matching file values. They are read once at startup.

Boolean variables accept `true`, `false`, `1`, or `0` (case-insensitive). Integer variables accept any parseable integer string.

| Variable | Config Equivalent | Type | Description |
|---|---|---|---|
| `MOTUS_HEADLESS` | `launch.headless` | `bool` | Run the browser headlessly. |
| `MOTUS_CHANNEL` | `launch.channel` | `string` | Browser channel (`Chrome`, `Edge`, `Chromium`, `Firefox`). |
| `MOTUS_SLOWMO` | `launch.slowMo` | `int` (ms) | Per-operation delay in milliseconds. |
| `MOTUS_LAUNCH_TIMEOUT` | `launch.timeout` | `int` (ms) | Browser startup timeout. |
| `MOTUS_LOCALE` | `context.locale` | `string` | BCP 47 locale tag for the browser context. |
| `MOTUS_COLOR_SCHEME` | `context.colorScheme` | `string` | Color scheme emulation (`Light`, `Dark`, `NoPreference`). |
| `MOTUS_IGNORE_HTTPS_ERRORS` | `context.ignoreHTTPSErrors` | `bool` | Ignore TLS certificate errors. |
| `MOTUS_LOCATOR_TIMEOUT` | `locator.timeout` | `int` (ms) | Default locator wait timeout. |
| `MOTUS_ASSERTIONS_TIMEOUT` | `assertions.timeout` | `int` (ms) | Default assertion wait timeout. |
| `MOTUS_FAILURES_SCREENSHOT` | `failure.screenshot` | `bool` | Take a screenshot on failure. |
| `MOTUS_FAILURES_SCREENSHOT_PATH` | `failure.screenshotPath` | `string` | Directory for failure screenshots. |
| `MOTUS_FAILURES_TRACE` | `failure.trace` | `bool` | Capture a trace archive on failure. |
| `MOTUS_FAILURES_TRACE_PATH` | `failure.tracePath` | `string` | Directory for trace archives. |
| `MOTUS_ACCESSIBILITY_ENABLE` | `accessibility.enable` | `bool` | Enable the accessibility audit hook. |
| `MOTUS_ACCESSIBILITY_MODE` | `accessibility.mode` | `string` | Audit violation handling mode (`Off`, `Warn`, `Enforce`). |

> Not all config file sections have environment variable coverage. `reporter`, `recorder`, and the locator `selectorPriority` arrays are file-only settings. Use the config file for those.

---

## Layered Resolution

Configuration is resolved in this order, where later layers take precedence:

```
motus.config.json  <  environment variables  <  LaunchOptions / ContextOptions (code)
```

**Layer 1 -- File.** `motus.config.json` provides project-wide baseline settings shared across all runs and committed to source control.

**Layer 2 -- Environment variables.** `MOTUS_*` variables overlay the file settings at runtime. This layer is intended for CI pipelines and per-machine overrides that should not be committed.

**Layer 3 -- Code.** `LaunchOptions` and `ContextOptions` values passed directly at call sites are the highest-priority layer. A value explicitly set in code is never overwritten by any lower layer.

---

## ConfigMerge Behavior

`ConfigMerge` applies the file-and-environment-resolved config onto a `LaunchOptions` or `ContextOptions` instance only when the instance still holds its default value. The rule is: **do not overwrite a value the caller already set**.

The merge checks are per-property:

- `Headless` -- the config value is applied only when `options.Headless` is still `true` (the default). If the caller passes `Headless = false`, the config file cannot re-enable it.
- `Channel` -- applied only when `options.Channel is null`.
- `SlowMo` -- applied only when `options.SlowMo == 0`.
- `Timeout` -- applied only when `options.Timeout == 30000` (the default).
- `Locale` -- applied only when `options.Locale is null`.
- `ColorScheme` -- applied only when `options.ColorScheme is null`.
- `IgnoreHTTPSErrors` -- a config value of `true` is applied when `options.IgnoreHTTPSErrors` is `false`. A config value of `false` never overrides a caller-set `true`.
- `Viewport` -- applied only when `options.Viewport is null`. Both `width` and `height` must be non-null in the config for a viewport to be applied.
- `Accessibility` -- the entire `AccessibilityOptions` object is applied only when `options.Accessibility is null`.

---

## LaunchOptions

`LaunchOptions` is a `sealed record` in `Motus.Abstractions`. Construct it with `with`-expressions or object initializer syntax.

| Property | Type | Default | Description |
|---|---|---|---|
| `Headless` | `bool` | `true` | Run the browser without a visible window. |
| `Channel` | `BrowserChannel?` | `null` | Browser distribution channel. Values: `Chrome`, `Edge`, `Chromium`, `Firefox`. |
| `ExecutablePath` | `string?` | `null` | Path to a browser executable. Overrides the bundled browser when set. |
| `Args` | `IReadOnlyList<string>?` | `null` | Additional command-line arguments passed to the browser process. |
| `SlowMo` | `int` | `0` | Milliseconds to add after every browser operation. |
| `Timeout` | `int` | `30000` | Maximum time in milliseconds to wait for the browser process to start. |
| `UserDataDir` | `string?` | `null` | Path to a browser user-data directory. Enables persistent sessions. |
| `HandleSIGINT` | `bool` | `true` | Close the browser when the process receives SIGINT. |
| `HandleSIGTERM` | `bool` | `true` | Close the browser when the process receives SIGTERM. |
| `IgnoreDefaultArgs` | `IReadOnlyList<string>?` | `null` | Default browser arguments to suppress. |
| `DownloadsPath` | `string?` | `null` | Directory for browser-initiated file downloads. |
| `Plugins` | `IReadOnlyList<IPlugin>?` | `null` | Plugin instances loaded into every browser context created from this launch. |
| `Accessibility` | `AccessibilityOptions?` | `null` | Accessibility audit hook configuration. Disabled when `null`. |

### AccessibilityOptions

Nested under `LaunchOptions.Accessibility`. A `null` value disables the hook entirely.

| Property | Type | Default | Description |
|---|---|---|---|
| `Enable` | `bool` | `false` | Master switch for the accessibility audit hook. |
| `Mode` | `AccessibilityMode` | `Enforce` | How violations are handled: `Off`, `Warn`, or `Enforce`. |
| `AuditAfterNavigation` | `bool` | `true` | Run an audit after each page navigation. |
| `AuditAfterActions` | `bool` | `false` | Run an audit after click, fill, and selectOption. |
| `IncludeWarnings` | `bool` | `true` | Count warning-severity findings as failures when mode is `Enforce`. |
| `SkipRules` | `IReadOnlyList<string>?` | `null` | Rule IDs excluded from every audit. |

---

## ContextOptions

`ContextOptions` is a `sealed record` in `Motus.Abstractions`. It describes a single browser context.

| Property | Type | Default | Description |
|---|---|---|---|
| `Viewport` | `ViewportSize?` | `null` | Viewport dimensions. `null` disables the default viewport. |
| `Locale` | `string?` | `null` | BCP 47 locale tag, e.g. `"en-US"`. |
| `TimezoneId` | `string?` | `null` | IANA timezone identifier, e.g. `"America/New_York"`. Not currently mapped from the config file; set in code. |
| `Geolocation` | `Geolocation?` | `null` | Geolocation to emulate. Not currently mapped from the config file; set in code. |
| `Permissions` | `IReadOnlyList<string>?` | `null` | Browser permissions granted to all pages in the context. Not currently mapped from the config file; set in code. |
| `ColorScheme` | `ColorScheme?` | `null` | Preferred color scheme. Values: `Light`, `Dark`, `NoPreference`. |
| `UserAgent` | `string?` | `null` | Custom user-agent string. Not currently mapped from the config file; set in code. |
| `IgnoreHTTPSErrors` | `bool` | `false` | Ignore TLS certificate errors on all requests. |
| `HttpCredentials` | `HttpCredentials?` | `null` | Credentials for HTTP authentication. Not currently mapped from the config file; set in code. |
| `Proxy` | `ProxySettings?` | `null` | Proxy settings for this context. Not currently mapped from the config file; set in code. |
| `RecordVideo` | `RecordVideoOptions?` | `null` | Video recording settings. |
| `StorageState` | `StorageState?` | `null` | Pre-seeded cookies and local storage. Not currently mapped from the config file; set in code. |
| `ExtraHttpHeaders` | `IDictionary<string, string>?` | `null` | Extra HTTP headers sent with every request. Not currently mapped from the config file; set in code. |
| `BaseURL` | `string?` | `null` | Base URL for relative navigations. Not currently mapped from the config file; set in code. |

---

## Example: CI Configuration via Environment Variables

The following shell snippet configures Motus for a headless CI run with aggressive diagnostics and no slow-down:

```sh
export MOTUS_HEADLESS=true
export MOTUS_CHANNEL=Chrome
export MOTUS_SLOWMO=0
export MOTUS_LAUNCH_TIMEOUT=60000
export MOTUS_ASSERTIONS_TIMEOUT=10000
export MOTUS_FAILURES_SCREENSHOT=true
export MOTUS_FAILURES_SCREENSHOT_PATH=test-results/failures
export MOTUS_FAILURES_TRACE=true
export MOTUS_FAILURES_TRACE_PATH=test-results/traces
export MOTUS_ACCESSIBILITY_ENABLE=true
export MOTUS_ACCESSIBILITY_MODE=Enforce
```

No `motus.config.json` is required on the CI agent. These variables provide a complete runtime configuration without touching source control.

---

## Example: motus.config.json for Headed Debugging

Use this config during local development to run in a visible browser window with slowed-down interactions and automatic screenshots on failure:

```json
{
  "motus": "1.0",
  "launch": {
    "headless": false,
    "channel": "Chrome",
    "slowMo": 150
  },
  "context": {
    "viewport": {
      "width": 1440,
      "height": 900
    }
  },
  "failure": {
    "screenshot": true,
    "screenshotPath": "test-results/debug-failures"
  },
  "assertions": {
    "timeout": 10000
  }
}
```

Commit this file or add it to `.gitignore` depending on whether the whole team uses the same local settings. The file is ignored by CI as long as those agents set `MOTUS_HEADLESS=true`, because environment variables take precedence over the file.

---

## See Also

- [Getting Started](../getting-started.md)
- [Plugins and Extensibility](../extensions/plugins.md)
- [Accessibility Auditing](./accessibility.md)
- [Reporters](./reporters.md)
