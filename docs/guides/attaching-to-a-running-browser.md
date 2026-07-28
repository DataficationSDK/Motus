# Attaching to a Running Browser

Motus can drive a browser it did not start. Point it at a browser's remote debugging endpoint and everything already open in that browser becomes visible and drivable: its contexts, its tabs, its signed-in sessions. Motus never ends a browser it did not start, so the connection can be dropped without disturbing whoever is using it.

This unlocks the cases a launched browser cannot cover: reusing a warm browser across many runs, driving a profile that is already signed in, sharing one browser between a person and an agent, and automating a Chromium-based desktop application that starts itself.

---

## Starting a browser with a debugging port

The endpoint is not open by default. The browser has to be started with the port, which is deliberate: see [Security](#security) below before enabling it on a browser you care about.

```bash
# macOS
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
  --remote-debugging-port=9222 --user-data-dir=/tmp/motus-profile

# Linux
google-chrome --remote-debugging-port=9222 --user-data-dir=/tmp/motus-profile

# Windows
"%ProgramFiles%\Google\Chrome\Application\chrome.exe" ^
  --remote-debugging-port=9222 --user-data-dir=%TEMP%\motus-profile
```

`--user-data-dir` is worth passing. Without it the browser reuses its ordinary profile, and if an instance of that profile is already running the new command hands its arguments to the existing process and exits, leaving no debugging port open at all.

The port is bound to loopback. `--remote-debugging-address` exists but binding it anywhere else exposes full control of the browser to the network; tunnel the loopback port instead.

---

## Connecting

```csharp
await using IBrowser browser = await MotusLauncher.ConnectAsync("http://127.0.0.1:9222");

foreach (IBrowserContext context in browser.Contexts)
{
    foreach (IPage page in context.Pages)
    {
        Console.WriteLine($"{await page.TitleAsync()} - {page.Url}");
    }
}
```

Either form of endpoint works. The HTTP debugging endpoint is the one you already know because you chose the port; the CDP WebSocket URL is accepted directly when you have it.

```csharp
await MotusLauncher.ConnectAsync("http://127.0.0.1:9222");                       // HTTP endpoint
await MotusLauncher.ConnectAsync("ws://127.0.0.1:9222/devtools/browser/a1b2c3");  // WebSocket URL
```

`ConnectAsync` returns once the browser's existing contexts and pages have been adopted, so `browser.Contexts` and `context.Pages` are populated immediately with no extra call. Adoption can be turned off when you only want a connection and intend to create your own contexts:

```csharp
await MotusLauncher.ConnectAsync(endpoint, new ConnectOptions
{
    AdoptExistingTargets = false,   // default: true
    Timeout = 30_000,               // milliseconds, bounds the whole connect
});
```

New tabs and frames that appear after connecting are picked up as they happen, so a long-lived session stays accurate rather than describing the browser as it was at connect time.

---

## Ownership

Ownership is the difference between a launched browser and an attached one, and it is observable:

```csharp
if (browser.OwnsProcess)
{
    // Motus started this browser and is responsible for ending it.
}
```

| Call | Launched browser | Attached browser |
|---|---|---|
| `CloseAsync` | Closes contexts, sends `Browser.close`, ends the process | Closes contexts Motus created, then disconnects. The browser keeps running |
| `DisconnectAsync` | Ends the connection, leaves the process running | Ends the connection |
| `DisposeAsync` | Kills the process without the graceful wait | Drops the transport |

`DisconnectAsync` exists so the intent is visible at the call site. On an attached browser it does the same thing `CloseAsync` does, and on a launched browser it is the way to walk away from a browser you started without ending it.

Pages adopted from an attached browser behave the same way. Closing a context Motus adopted releases its pages from Motus and unloads plugins, but does not dispose the browser context or close the windows, because they belong to whoever was using the browser first.

### When the connection drops

If the browser exits, or the WebSocket is lost, `IBrowser.IsConnected` goes false and the `Disconnected` event fires. Handles taken before that point are not silently wrong; operations on them fail with a disconnection error. For an attached browser, a lost connection says nothing about whether the browser is still there, so reconnecting to the same endpoint is the correct response and will simply fail if the browser really is gone.

---

## Patterns

### Reusing a warm browser between runs

Start the browser once, then run the suite against it as many times as you like. Startup cost is paid once instead of per run, and the profile keeps its cache and any sign-in the first run performed.

```csharp
await using IBrowser browser = await MotusLauncher.ConnectAsync("http://127.0.0.1:9222");
await using IBrowserContext context = await browser.NewContextAsync();
IPage page = await context.NewPageAsync();
```

Creating a context gives the run isolation from whatever else is in the browser, and closing it at the end leaves the browser as it was.

### CI and containers

Run the browser as its own service and connect to it, rather than installing and launching a browser inside the job. The browser image is pinned separately from the test image, and a browser crash cannot take the job's process with it.

```yaml
services:
  chrome:
    image: chromium-with-debugging:pinned
    ports:
      - "9222:9222"
```

```csharp
string endpoint = Environment.GetEnvironmentVariable("BROWSER_ENDPOINT") ?? "http://127.0.0.1:9222";
await using IBrowser browser = await MotusLauncher.ConnectAsync(endpoint);
```

Publish the port to loopback on the host only. A debugging port reachable from the CI network is reachable by every other job on it.

### Driving a profile that is already signed in

Sign in by hand once, in a browser started with a debugging port and a persistent `--user-data-dir`, then attach for every subsequent run. The tests never handle credentials, and flows that resist automation entirely, such as a hardware second factor, are performed once by a person rather than reproduced in code.

Treat that profile as a credential. It holds live session cookies for every site it signed into.

### Chromium-based desktop applications

Applications built on an embedded Chromium runtime usually expose the same debugging port, typically through a command-line flag or an environment variable the application defines. Once the port is open, such an application is an ordinary target: its window is a page, its UI is a DOM, and its panels are frames.

Two differences are worth knowing before you start:

- **Contexts may not be creatable.** Some embedded hosts do not support `Target.createBrowserContext`, so `NewContextAsync` fails. Work in the context the application already has, which is what adoption gives you.
- **The application starts itself.** Motus attaches to an endpoint; starting the application, and knowing when its endpoint is ready, is the caller's job. Poll the endpoint rather than sleeping.

---

## Through the MCP server

The server can be started attached, so every tool call acts on a browser that is already running:

```bash
motus mcp --connect http://127.0.0.1:9222
```

Or an agent can attach at any point with the `browser_attach` tool, which is the option to reach for when the endpoint is not known at the time the MCP client is configured. `browser_status` reports which browser is being driven, whether it was started by the server, and how many contexts and tabs are open.

Two consequences of attaching are worth stating plainly:

- **Options that describe starting a browser have nothing to act on.** `--headless`, `--channel`, `--viewport`, `--record-video` and `--show-cursor` all bind either at launch or at context creation, and an attached session does neither. The server says so on startup rather than ignoring them silently. The `resize` tool still changes a page's viewport at runtime.
- **`--http` and `--connect` together share one browser.** The HTTP transport otherwise gives each connected client its own isolated browser. Pointed at one endpoint, every session drives the same browser, and so shares its tabs and cookies.

The destructive tools mean more when attached. `tab_close` and `context_close` discard somebody's working state rather than scratch state.

---

## Security

A remote debugging endpoint grants complete control of the browser and of every session inside it, including authenticated ones. Anything that can reach the port can read cookies, issue requests as the signed-in user, read page contents, and execute script in any origin the browser has open.

- **Bind it to loopback.** This is the default. Do not move it without a tunnel in front of it.
- **Do not enable it on a browser holding credentials you would not hand to a script.** A profile with live sessions for your email, source control, or cloud console is exactly that.
- **Treat attaching as equivalent to sitting at the machine.** There is no narrower permission to grant. The endpoint is all or nothing.
- **Use a dedicated profile.** A `--user-data-dir` created for automation limits what a mistake can reach.

---

## Known traps

**The endpoint's target list is not a list of a page's frames.** `http://127.0.0.1:9222/json/list` reports one entry per CDP target. A frame the browser put in its own process appears there, typed `iframe` rather than `page`, and a frame the page's own renderer hosts does not appear at all, because it has no target. Checking the list by hand will undercount the frames on a page, and filtering it for `page` entries will miss every frame. Motus reports both kinds in `page.Frames`, so read frames from there. See [Frames and iframes](frames-and-iframes.md).

**A browser already running under its ordinary profile ignores the flag.** As above: the second command hands off to the running instance and exits, and no port opens. Check that the endpoint answers before concluding Motus cannot reach it.

```bash
curl http://127.0.0.1:9222/json/version
```

---

## What's next

- [Frames and iframes](frames-and-iframes.md) -- what to do once attached to an application whose interface is built from frames
- [Browser Lifecycle](../architecture/browser-lifecycle.md) -- how attaching and adoption work underneath
- [MCP Server](mcp-server.md) -- the full tool catalog and server options
- [Transport and Protocol](../architecture/transport-and-protocol.md) -- flattened sessions and the session-per-target model
