# Browser Lifecycle

This document describes how Motus creates, manages, and tears down browser instances. It covers the full path from calling `MotusLauncher.LaunchAsync` through to disposal, including browser discovery, the object hierarchy, context and page initialization, signal handling, and the optional browser pool.

---

## Launching a browser

`MotusLauncher.LaunchAsync` in `Motus.cs` is the primary entry point.

```csharp
IBrowser browser = await MotusLauncher.LaunchAsync(new LaunchOptions
{
    Channel = BrowserChannel.Chrome,
    Headless = true,
});
```

The launch sequence runs in the following order:

1. **Config merge.** `ConfigMerge.ApplyConfig(options)` overlays values from `motus.json` (loaded by `MotusConfigLoader`) onto the caller-supplied `LaunchOptions`. File-level settings win only when the programmatic option is still at its default value, so explicit caller values are never overwritten.

2. **Executable resolution.** `BrowserFinder.Resolve(options.Channel, options.ExecutablePath)` locates the binary. See [Browser discovery](#browser-discovery) below.

3. **Firefox detection.** `IsFirefoxChannel` returns `true` when the channel is `BrowserChannel.Firefox` or when the resolved executable filename contains `"firefox"` (case-insensitive). This flag drives every subsequent branch.

4. **Port allocation.** `AllocateFreePort()` binds a `TcpListener` on `IPAddress.Loopback:0`, reads the OS-assigned port from `LocalEndpoint`, then immediately stops the listener. The port is passed to the browser as the remote debugging port.

5. **Profile directory.**
   - For Chromium channels: if `options.UserDataDir` is `null`, a temporary directory is created at `{Path.GetTempPath()}/motus-profile-{8-char guid}`. The `ownsTempDir` flag is set to `true` so the directory is deleted on cleanup.
   - For Firefox: `FirefoxProfileManager.CreateTempProfile` creates a minimal profile directory unconditionally when no `UserDataDir` is supplied.

6. **Process start.** A `ProcessStartInfo` is built with `UseShellExecute = false`, `RedirectStandardOutput = true`, and `RedirectStandardError = true`. Arguments are added via `ArgumentList` (not `Arguments`) so that paths with spaces are handled correctly without manual quoting. For Firefox, environment variables from `FirefoxArgs.Build` are also applied.

7. **Transport connection.**
   - **Chromium:** `CdpEndpointPoller.WaitForEndpointAsync` polls the `/json/version` HTTP endpoint on the allocated port until the browser is ready, then returns a WebSocket URL. A `CdpTransport` wraps a `CdpSocket` and connects to that URL. An optional `SlowMo` delay (milliseconds) is wired into the transport.
   - **Firefox:** `FirefoxEndpointReader.WaitForEndpointAsync` reads the process's stderr stream to discover the WebSocket BiDi endpoint. A `BiDiTransport` connects and negotiates an initial session.

8. **Browser object construction.** A `Browser` instance is created with the transport, session registry, process handle, temp dir path (if owned), and signal-handler flags. `InitializeAsync` then sends `Browser.getVersion` over the browser-level CDP/BiDi session to populate `IBrowser.Version` and sets `IsConnected = true`.

**Error handling:** if any step after process start throws, the process is killed with `entireProcessTree: true`, disposed, and the temp directory deleted before the exception propagates.

---

## Connecting to an existing browser

`MotusLauncher.ConnectAsync` attaches to a browser that is already running and listening for CDP connections.

```csharp
IBrowser browser = await MotusLauncher.ConnectAsync("ws://localhost:9222/devtools/browser/...");
```

The sequence is simpler than a launch:

1. A `CdpSocket` and `CdpTransport` are created without a `SlowMo` delay.
2. The transport connects directly to the supplied WebSocket URI.
3. A `CdpSessionRegistry` wraps the transport.
4. A `Browser` is constructed with `process: null` and `tempUserDataDir: null`. Signal handlers are not registered (`handleSigint: false`, `handleSigterm: false`) because Motus does not own the process.
5. `InitializeAsync` runs identically to the launch path.

Because no process or temp directory is owned, `DisposeAsync` on a connected browser only closes the WebSocket transport.

---

## Browser discovery

`BrowserFinder.Resolve` determines the executable path using the following precedence:

1. **Explicit path.** If `options.ExecutablePath` is set, it is validated with `File.Exists` and returned directly. A `FileNotFoundException` is thrown if the file does not exist.

2. **Channel-specific candidates.** If `options.Channel` is set, `CandidatesForChannel` builds an ordered list of paths for the current platform and returns the first one that exists.

3. **Auto-detect.** If neither option is set, `Resolve` iterates over `Chrome`, `Edge`, and `Chromium` in that order, returning the first match found.

### `InstalledBinariesPath`

When the Motus install system (Phase 3D) downloads a browser, it sets `BrowserFinder.InstalledBinariesPath`. This path is prepended to every channel's candidate list, ahead of system-installed locations.

### Platform candidate paths

| Channel | macOS | Linux | Windows |
|---|---|---|---|
| Chrome | `/Applications/Google Chrome.app/Contents/MacOS/Google Chrome` | `/usr/bin/google-chrome-stable`, `/usr/bin/google-chrome` | `%ProgramFiles%\Google\Chrome\Application\chrome.exe`, `%ProgramFiles(x86)%\...`, `%LocalAppData%\...` |
| Edge | `/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge` | `/usr/bin/microsoft-edge-stable`, `/usr/bin/microsoft-edge` | `%ProgramFiles%\Microsoft\Edge\Application\msedge.exe`, `%ProgramFiles(x86)%\...` |
| Chromium | `/Applications/Chromium.app/Contents/MacOS/Chromium` | `/usr/bin/chromium-browser`, `/usr/bin/chromium` | `%LocalAppData%\Chromium\Application\chrome.exe` |
| Firefox | `/Applications/Firefox.app/Contents/MacOS/firefox`, `/Applications/Firefox Nightly.app/...` | `/usr/bin/firefox`, `/usr/bin/firefox-esr`, `/usr/local/bin/firefox`, `/snap/bin/firefox` | `%ProgramFiles%\Mozilla Firefox\firefox.exe`, `%ProgramFiles(x86)%\...` |

---

## Object hierarchy

```
IBrowser
  └─ IBrowserContext  (one or more)
       └─ IPage       (one or more)
```

Ownership and lifetime rules:

- `IBrowser` owns all `IBrowserContext` instances. `CloseAsync` on the browser closes all contexts in order before sending `Browser.close`.
- `IBrowserContext` owns all `IPage` instances. Closing a context closes all its pages, then sends `Target.disposeBrowserContext`.
- `IPage` is owned by the context that created it. A page closed independently (via `IPage.CloseAsync`) removes itself from the context's page list and sends `Target.closeTarget`, but does not close the context.
- `IBrowser` also owns the OS process (when launched) and the temp profile directory (when `UserDataDir` was not supplied by the caller).

The `IBrowserContext.Browser` property provides upward navigation. `IPage.Context` provides upward navigation from a page to its owning context.

---

## Context creation

`IBrowser.NewContextAsync` creates an isolated browsing context.

```csharp
IBrowserContext context = await browser.NewContextAsync(new ContextOptions
{
    Viewport = new ViewportSize(1280, 720),
    Locale = "en-US",
});
```

Internally, `Browser.NewContextAsync` runs these steps:

1. `ConfigMerge.ApplyConfig(options)` merges `motus.json` context defaults.

2. The CDP command `Target.createBrowserContext` is sent on the browser-level session with `DisposeOnDetach: true`. This creates a new isolated context in the browser process and returns a `browserContextId`.

3. A `BrowserContext` is instantiated with the registry, the context ID, and the resolved options. It does not yet contain any pages.

4. **Plugin loading.** A `PluginHost` is created and `LoadAsync` is called:
   - Built-in plugins (`BuiltinSelectorsPlugin`, `AccessibilityRulesPlugin`, `AccessibilityAuditHook`) are loaded unconditionally. Failures propagate and abort context creation.
   - Auto-discovered plugins registered via `PluginDiscovery.Factory` are merged with any plugins supplied in `LaunchOptions.Plugins`. Manual registrations take precedence in deduplication by `PluginId`.
   - User and discovered plugins are loaded with per-plugin failure isolation: a plugin that throws `OnLoadedAsync` is silently skipped.
   - Plugins are loaded in registration order; they are unloaded in reverse order when the context closes.

5. The context is added to `Browser._contexts`.

6. If `ContextOptions.Permissions` contains entries, `Browser.grantPermissions` is sent immediately.

The convenience method `IBrowser.NewPageAsync` calls `NewContextAsync` followed by `context.NewPageAsync`, creating a page in a fresh isolated context.

---

## Page creation

`IBrowserContext.NewPageAsync` creates a new tab within the context.

```csharp
IPage page = await context.NewPageAsync();
```

The sequence:

1. `Target.createTarget` is sent with `url: "about:blank"` and the parent `BrowserContextId`. The browser returns a `targetId`.

2. `Target.attachToTarget` is sent with `flatten: true`. The browser returns a `sessionId` for the new page's CDP session.

3. A `Page` object is constructed around the new session and target ID, then `Page.InitializeAsync` runs:

   a. The event pump is started **before** any CDP domain is enabled. This is necessary because Chrome fires `Runtime.executionContextCreated` immediately when `Runtime.enable` is sent. If the pump were not already listening, that first event and the main-frame context ID would be dropped.

   b. `Page.enable` is sent to enable page lifecycle events.

   c. `Runtime.enable` is sent to enable JavaScript context tracking.

   d. `Page.setInterceptFileChooserDialog` is sent with `enabled: true` to capture file chooser dialogs.

   e. Any init scripts registered on the context via `AddInitScriptAsync` are installed with `Page.addScriptToEvaluateOnNewDocument`.

   f. Any bindings registered on the context via `ExposeBindingAsync` are applied with `Runtime.addBinding`.

   g. Network monitoring and interception are initialized via `InitializeNetworkAsync`.

4. Context options are applied to the new page: viewport (`Emulation.setDeviceMetricsOverride`), locale, timezone, color scheme, user agent, HTTPS error handling, and geolocation are each sent if the corresponding option is set.

5. Context-level extra HTTP headers and offline state are propagated to the page.

6. If `ContextOptions.StorageState` is set and this is the first page created in the context, cookies and `localStorage` entries are restored.

7. If `ContextOptions.RecordVideo` is set, a `VideoRecorder` is started.

8. The page is added to `BrowserContext._pages`, lifecycle hooks fire `OnPageCreated`, and the `IBrowserContext.Page` event is raised.

---

## Cleanup and disposal

### `IBrowser.CloseAsync`

`CloseAsync` is the graceful shutdown path:

1. All contexts are closed in sequence by calling `context.CloseAsync()`.
2. `Browser.close` is sent over the browser-level CDP session. A `CdpDisconnectedException` is expected and caught because the browser closes the WebSocket as part of its shutdown.
3. The owned OS process (if any) is awaited for up to **5 seconds**. If it has not exited within that window, `process.Kill(entireProcessTree: true)` is called.
4. `IsConnected` is set to `false`.

### `IBrowserContext.CloseAsync`

1. `PluginHost.UnloadAsync` is called first, unloading plugins in reverse registration order. Individual plugin failures are swallowed so all plugins receive `OnUnloadedAsync`.
2. Each page has lifecycle hooks fired (`OnPageClosed`), then `page.DisposeAsync` is called. The CDP session and its event channels are cleaned up.
3. `Target.disposeBrowserContext` is sent to the browser.
4. The `IBrowserContext.Close` event is raised.

### `IBrowser.DisposeAsync`

`DisposeAsync` is the non-graceful teardown path, used when an exception occurs during launch or when the pool discards a disconnected browser:

1. Signal handlers are unregistered.
2. `IsConnected` is set to `false`.
3. The transport is disposed asynchronously.
4. If the process has not exited, `process.Kill(entireProcessTree: true)` is called immediately, without the 5-second grace period.
5. The process handle is disposed.
6. If a temp user data directory is owned, `Directory.Delete(path, recursive: true)` is attempted. Failures are silently ignored (best-effort cleanup).

### Signal handlers

When a browser is launched (not connected), signal handlers are registered if the corresponding `LaunchOptions` flags are set:

- `HandleSIGINT` (default `true`): hooks `Console.CancelKeyPress`, cancels the event (preventing immediate process exit), and calls `CloseAsync` asynchronously.
- `HandleSIGTERM` (default `true`): hooks `AppDomain.CurrentDomain.ProcessExit` and calls `CloseAsync` asynchronously.

Both handlers are removed in `DisposeAsync` via `UnregisterSignalHandlers`. Signal handlers are never registered for browsers obtained via `ConnectAsync`.

---

## Browser pooling

`MotusBrowserPool` manages a pool of reusable browser instances for concurrent workloads such as parallel test execution.

```csharp
await using IBrowserPool pool = await MotusBrowserPool.CreateAsync(new BrowserPoolOptions
{
    MinInstances = 2,
    MaxInstances = 8,
    AcquireTimeout = TimeSpan.FromSeconds(30),
    LaunchOptions = new LaunchOptions { Headless = true },
});

await using IBrowserLease lease = await pool.AcquireAsync();
IPage page = await lease.Browser.NewPageAsync();
// ... use page ...
// Lease returns browser to pool on DisposeAsync
```

### `BrowserPoolOptions`

| Property | Default | Description |
|---|---|---|
| `MinInstances` | `1` | Number of browsers launched eagerly during `WarmUpAsync`. |
| `MaxInstances` | `Environment.ProcessorCount` | Hard cap on concurrent browser instances. |
| `AcquireTimeout` | `30s` | How long `AcquireAsync` waits when all instances are busy. |
| `LaunchOptions` | `null` | Launch settings applied to every browser in the pool. |

### Internals

`BrowserPool` uses two concurrency primitives:

- A `SemaphoreSlim` initialized to `MaxInstances` tracks total capacity.
- An unbounded `Channel<IBrowser>` acts as the idle queue.

`MotusBrowserPool.CreateAsync` constructs the pool and calls `WarmUpAsync`, which launches `MinInstances` browsers in parallel, each acquiring a semaphore slot before launching.

**`AcquireAsync` flow:**

1. Attempt to dequeue an idle browser immediately without blocking.
2. If no idle browser is available and capacity remains, launch a new browser.
3. If at capacity, wait for up to `AcquireTimeout` for a browser to be returned.
4. Any idle browser that is found to be disconnected is disposed and its semaphore slot released.

The returned `IBrowserLease` holds the browser and a return callback. Disposing the lease returns the browser to the idle channel if it is still connected and the pool has not been disposed. A disconnected or post-dispose browser is discarded and the semaphore slot is released.

`IBrowserPool.ActiveCount` and `IdleCount` expose live counters for observability.

`DisposeAsync` on the pool drains and disposes all idle browsers. Browsers that are currently leased out are not forcibly recalled; callers must dispose their leases before the pool can be fully cleaned up.

---

## See Also

- `src/Motus/Motus.cs` - `MotusLauncher` source
- `src/Motus/Browser/BrowserFinder.cs` - platform executable discovery
- `src/Motus/Context/BrowserContext.cs` - context and page creation
- `src/Motus/Page/Page.cs` - page initialization and CDP domain enablement
- `src/Motus/Pool/MotusBrowserPool.cs` and `BrowserPool.cs` - pool implementation
- `src/Motus/Plugins/PluginHost.cs` - plugin loading and unloading
- `src/Motus.Abstractions/IBrowser.cs`, `IBrowserContext.cs`, `IPage.cs` - public contracts
- `src/Motus.Abstractions/IBrowserPool.cs` - pool and lease contracts
- `src/Motus.Abstractions/Options/LaunchOptions.cs` - launch configuration reference
- `src/Motus.Abstractions/Options/BrowserPoolOptions.cs` - pool configuration reference
- `docs/architecture/overview.md` - high-level architecture overview
