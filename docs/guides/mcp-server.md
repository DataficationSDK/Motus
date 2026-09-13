# MCP Server

Motus ships a [Model Context Protocol](https://modelcontextprotocol.io) server so AI agents can drive a real browser through the same engine that powers the test framework. The server is not a separate download. It is a verb on the CLI tool: `motus mcp`. Once `Motus.Cli` is installed as a global tool, any MCP client (Claude Code, Claude Desktop, or anything that speaks the protocol) can launch the server and call its tools.

The server exposes browser automation as structured tools: navigate a page, take an accessibility snapshot, click and type against elements, intercept network traffic, run accessibility and performance audits, record traces, and generate Page Object Model code. Perception is built on the browser's accessibility tree rather than raw pixels, so an agent reasons over a compact, labeled element list and addresses elements by stable reference.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later. The tool targets `net8.0` and rolls forward, so a machine with only the .NET 10 runtime works as well.
- The `Motus.Cli` global tool.
- A browser. `motus install` downloads Chromium; the server can also drive an already-installed Chrome, Edge, or Firefox.

```bash
dotnet tool install --global Motus.Cli
motus install
```

`motus install` places a browser under `~/.motus/browsers`. When the server starts it prefers a browser installed this way and otherwise falls back to a system browser, so this step is recommended but not strictly required if you already have a supported browser on the machine.

---

## Registering with Claude Code

Claude Code's CLI registers MCP servers with `claude mcp add`. Point it at the installed tool and pass the `mcp` subcommand after `--`:

```bash
claude mcp add motus -- motus mcp
```

This records a server named `motus` whose launch command is `motus mcp` over stdio. Everything after `--` is the command Claude Code runs to start the server, so any server option goes there too:

```bash
# Drive Chrome instead of the downloaded Chromium
claude mcp add motus -- motus mcp --channel chrome

# Run with a visible browser window for debugging
claude mcp add motus -- motus mcp --headless false
```

List and inspect the registration:

```bash
claude mcp list
claude mcp get motus
```

Remove it with `claude mcp remove motus`.

If Claude Code reports that the `motus` server failed to connect, the usual cause is that `motus` is not on the `PATH` that Claude Code inherits, so the launch command cannot be found. Confirm it resolves:

```bash
which motus   # should print a path such as ~/.dotnet/tools/motus
```

If that prints nothing, the .NET global tools directory is not on your `PATH` (a common environment gap, not specific to Motus). Either add it to your `PATH`, or register the server with the absolute path so launching does not depend on `PATH`:

```bash
claude mcp add motus -- "$HOME/.dotnet/tools/motus" mcp
```

`dotnet tool list --global` reports the install location if it differs from the default above.

Once registered, start (or restart) Claude Code and the `motus` tools become available. Ask the agent to navigate to a page and it will call `navigate`, then `snapshot` to read the result.

### Other MCP clients

Clients that read a JSON configuration file (Claude Desktop and most others) take the same command and arguments. Add an entry under the client's `mcpServers` map:

```json
{
  "mcpServers": {
    "motus": {
      "command": "motus",
      "args": ["mcp"]
    }
  }
}
```

If `motus` is not on the client's `PATH`, use the absolute path to the installed tool (`~/.dotnet/tools/motus` on most systems, or the location reported by `dotnet tool list --global`) as `command`.

---

## Verifying the server

You can confirm the server starts and advertises its tools without an agent. The server speaks newline-delimited JSON-RPC over stdin and stdout, so a short handshake piped into `motus mcp` is enough:

```bash
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"1.0"}}}' \
  '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' \
  | motus mcp
```

The server responds with an `initialize` result identifying itself as `motus`, followed by a `tools/list` result enumerating every available tool. A navigate-then-snapshot exchange exercises a live browser end to end: send a `tools/call` for `navigate` with a `url`, wait for the browser to launch, then a `tools/call` for `snapshot`. The snapshot result is an indented accessibility tree with a `[ref=...]` on each addressable node, which is what an agent uses to target elements.

---

## Tools

The server groups its tools by capability. Each tool returns structured content and never throws across the protocol boundary; failures come back as an error result the agent can read.

| Area | Tools |
|---|---|
| Navigation | `navigate`, `go_back`, `go_forward`, `reload`, `wait_for` |
| Perception | `snapshot`, `screenshot`, `audit_accessibility`, `get_performance` |
| Interaction | `click`, `type`, `press`, `press_key`, `hover`, `focus`, `clear`, `select_option`, `set_checked`, `scroll_into_view`, `upload_files`, `wait_for_element` |
| Coordinate interaction | `click_xy`, `hover_xy`, `move_xy`, `scroll_xy`, `drag`, `resize` |
| Tabs and contexts | `tab_list`, `tab_open`, `tab_select`, `tab_close`, `context_list`, `context_create`, `context_select`, `context_close` |
| Frames | `frame_list`, `frame_select` |
| Browser | `browser_attach`, `browser_status` |
| Scripting | `evaluate` |
| Dialogs | `handle_dialog` |
| Network | `route_fulfill`, `route_abort`, `route_continue`, `unroute`, `route_list`, `network_requests`, `network_request` |
| Console | `console_messages` |
| Recording and codegen | `generate_pom`, `trace_start`, `trace_stop`, `har_start`, `har_stop`, `video_start`, `video_stop` |

Elements are addressed by the `ref` values returned in a snapshot, or by a selector. Take a `snapshot`, then pass a node's `ref` to `click`, `type`, or another interaction tool. References are relative to the most recent snapshot, so take a fresh snapshot after the page changes.

Anywhere a `ref` is accepted, a selector is accepted in its place: CSS by default (`#submit`, `button.primary`), or prefixed with `xpath=`, `text=`, `role=`, or `data-testid=`. A selector needs no snapshot at all, so it is what to reach for when the refs in hand have gone stale, or when you already know a stable selector for the element and would rather say it than look it up. Anything shaped like a ref (`e5`, or `f1e5` for an element inside a frame) is read as one, so a ref the latest snapshot no longer holds still comes back as a stale ref rather than as a selector that matched nothing. A selector is searched in the selected frame when one is selected, and in the page otherwise.

`click` also takes `button` (`left`, `right`, or `middle`) and `modifiers` (any of `Alt`, `Control`, `Meta`, `Shift`), so a context menu or a ctrl-click is reachable on an element rather than only at a coordinate. Both run the same actionability checks as a plain click: the element has to be visible, enabled, settled, and receiving events first. A double-click is left-button only; use `click_xy` for a double-click with a button or modifiers.

### What a snapshot contains

A snapshot is one line per node, indented to show nesting: the role, the accessible name in quotes, and then attributes in square brackets. A ref (`e1`, `e2`, ...) is attached to every node an agent might act on: interactive roles such as `button`, `link`, `textbox`, `combobox`, `checkbox`, `option`, `tab`, `menuitem`, `row`, and `cell`; any node with a name; anything focusable; and each `Iframe`. Wrappers, labels, and text carry no ref and are there for context. The attributes printed are `[level=N]` on headings, `[url=...]` on links, `[value="..."]` on form controls, `[frame=N]` on an `iframe` whose content follows underneath, and the state flags `[disabled]`, `[readonly]`, `[required]`, `[checked]`, `[selected]`, `[expanded]`, and `[pressed]` when set.

Text is folded into the element it names, so `button "Submit"` is one line rather than an element, its text, and the text's layout. Text that says something more than the name is printed after a colon (`listitem: Item one`), and text that sits between elements gets a `text:` line of its own so the order survives. An unnamed wrapper with a single child steps aside for it. The result on a long article is about a third the size of the raw tree, with refs on a quarter of its nodes.

A node the snapshot gave no ref is still reachable: pass a selector for it instead, which is also how to act on a page whose tree carries nothing addressable at all.

`audit_accessibility` reports each violation with a `ref` when the snapshot gave the node one. A node the snapshot does not address, such as an image with no alt text or an empty landmark, has `ref` set to null and is identified by `nodeRole`, `nodeName`, `nodeText`, and a best-effort `selector` instead.

### What an action reports

An action returns one line saying what it did, and under it a short block naming anything it changed. Only the rows that apply are printed, so an action on a page that does nothing surprising stays a single line:

```
click(e10)
  -> Clicked e10
```

A click that does more says so in the same result, which saves the snapshot, console read and tab listing it would otherwise take to find out:

```
click(e3)
  -> Clicked e3
     Page: https://example.com/checkout | Checkout
     New tab opened: [1] https://example.com/terms
     Console: 1 error, 1 page error. Read them with console_messages since=14.
     Refs from the last snapshot no longer address this page: it navigated. Take a new snapshot.
```

The rows are the page and its title when either changed, a tab the page opened with the index `tab_select` takes, how many errors and uncaught page errors the action logged along with the cursor that reads exactly those, a dialog the action left open, and a note that the refs in hand no longer mean anything because the page navigated. A frame that navigated on its own is reported the same way, by the index its refs carry, since the page's own address would show nothing. An action that fails reports why it failed and nothing else.

The browser accepts a click before it has followed the link the click was on, so the result waits briefly for the page to show what the action did: 500 ms by default, ending early when a tab appears. `--settle` changes the wait, and `--settle 0` writes the result the instant the action returns, which is right for a local page that reacts at once and wrong for one that navigates through a slow server.

`click`, `type`, `press`, `select_option`, `set_checked`, `navigate`, `reload`, `go_back` and `go_forward` also take `snapshot: true`, which appends a fresh snapshot of the page after the report. It is off by default: most actions do not change enough of the page to be worth a tree, and the rows above usually say whether this one did.

### Reading the console and network logs

`console_messages` and `network_requests` each keep the most recent 250 entries of what the active tab has done, and reading them leaves them in place. Each read ends with a `next=N` line; pass that back as `since` to read only what has arrived since:

```
console_messages()
  -> [error] Cannot read properties of null
     [pageerror] Error: uncaught boom
     next=3

console_messages(since: 3)
  -> No console messages have been logged since 3.
     next=3
```

A read that never reaches the agent can therefore simply be made again, and an action result that counts errors gives the `since` value that returns exactly those. When a busy page has pushed entries out of the log before they were read, the read says how many it missed.

`network_requests` prints a sequence number in front of each line. `network_request` takes one of those numbers and returns the request in full: its method, status, URL and resource type, the request and response headers, the request body, and the response body when the browser can still produce it. Bodies are fetched when they are asked for rather than captured with the entry, so a request belonging to a document the page has navigated away from reports that the browser no longer has it.

### Reading a value with `evaluate`

`evaluate` returns whatever the expression produced under a `result` key:

```
evaluate("document.querySelectorAll('article').length")
  -> {"result": 12}
```

Structured content has to be a JSON object, so an expression returning a bare number, string, or array could not be sent back as-is. Wrapping every value in the same shape means an expression may return anything: reading a single count off the page works exactly as readily as returning a record. A value that cannot be serialized, such as `undefined`, a function, or a DOM node, comes back as `{"result": null}`.

Read the value at `result`. Wrapping the expression by hand, as `({ count: ... })`, is no longer necessary, though it remains harmless and simply nests one level deeper.

### Dialogs

A JavaScript dialog stops the browser answering anything until it is handled, including the command that opened it. So an action that raises one comes back at once, saying what opened:

```
click(e12)
  -> The action opened an alert dialog: "Are you sure?". Call handle_dialog to accept or dismiss it.
```

Results from calls that follow carry a line of their own until the dialog is answered, and an action asked for while one is open is refused rather than sent into a page that cannot receive it:

```
A "confirm" dialog is open: "Delete this?". Handle it with handle_dialog before other actions.
```

`handle_dialog` takes `accept` and, for a prompt, the `text` to enter. Once it returns, the page carries on from where the dialog stopped it, so the work the action started finishes then rather than at the moment of the click. Reading the page is blocked in the same way, so `snapshot`, `screenshot` and `evaluate` report the dialog instead of waiting on it.

### Frames

A page snapshot contains the frames the page hosts. Each frame's content is printed inside the `iframe` element that holds it, so a payment form in a frame reads like the rest of the page and a click inside it takes no more calls than a click outside it:

```
- main
  - heading "Checkout" [ref=e4] [level=1]
  - Iframe "Payment frame" [ref=e14] [frame=1]
    - textbox "Card number" [ref=f1e1]
    - button "Pay now" [ref=f1e2]
```

The `[frame=N]` on the `iframe` line is the frame's index, and it is the prefix on every ref inside: `f1e2` is the second addressable element of frame 1. Pass that ref to `click` or `type` like any other and it acts inside the frame, with no frame selected and nothing else to set up. A frame nested inside a frame is printed inside its own parent, as deep as the page nests them, and its refs carry its own index.

The browser does not hand frames over with the page, so each one is read separately and stitched in. Ten of them are read by default, which covers ordinary pages; `max_frames` on `snapshot` raises or lowers that. Past the limit, and for a frame whose element is hidden or whose content could not be read, the snapshot says how many frames were left out and points at the tools below. `max_depth` counts a frame's content as levels of the tree like anything else, so a shallow snapshot stops at the `iframe` line.

Two tools remain for the things a ref cannot do:

1. `frame_list` lists the frames in document order with their nesting depth. Index 0 is the page itself, and each index is the one the snapshot printed as `[frame=N]`.
2. `frame_select <index>` scopes the session to one frame. `frame_select 0` returns to the page.

Scope is what `evaluate` and the `wait_for` text conditions need, since those name no element and so have no frame of their own to work from. It also narrows `snapshot`, which then describes that one frame on its own with plain `e1`, `e2` refs, and it decides where a selector is searched. Refs need no scope either way: a ref keeps addressing the document it was read from, even after the scope moves on. Selection resets on navigation and on switching tab or context, since the frame it named is gone by then.

A frame can navigate on its own without the page moving at all, which leaves the refs inside it addressing a document that is no longer there. When that happens, the next action says so by frame index, the same way it reports a page that navigated.

The coordinate tools stay in page coordinates whatever is selected. Their input is dispatched at the page level and the browser decides which frame is under the point. See [Frames and iframes](frames-and-iframes.md).

### Driving a browser that is already running

By default the server starts a browser and ends it on shutdown. Pointed at a browser somebody else started, it drives what is already open in that browser and leaves it running afterwards. This is how a signed-in profile, a warm browser reused between runs, or a Chromium-based desktop application becomes drivable.

```bash
motus mcp --connect http://127.0.0.1:9222
```

An agent can also attach at any point with `browser_attach`, which is what to reach for when the endpoint is not known at the time the MCP client is configured. That tool refuses unless the server was started with `--allow-attach` or `--connect`: a browser that is already running may hold somebody's signed-in sessions, so which browser the session drives stays the operator's decision. `browser_status` reports which browser is being driven and whether the server started it. Attaching closes the browser the server started, if any, and drops snapshot refs, route rules, and captured console output, so take a fresh snapshot afterwards.

Two consequences are worth knowing:

- **Options that describe starting a browser have nothing to act on.** `--headless`, `--channel`, `--executable-path`, `--browser-arg`, `--user-data-dir`, `--viewport`, `--storage-state`, `--user-agent`, `--locale`, `--timezone`, the proxy options, `--record-video` and `--show-cursor` bind either at launch or at context creation, and an attached session does neither: it adopts the context the browser is already using. The server says so on startup rather than ignoring them silently. The `resize` tool still changes a page's viewport at runtime.
- **`--http` with `--connect` means clients share one browser.** The HTTP transport otherwise gives each connected client its own isolated browser. Pointed at one endpoint, every session drives the same browser, and so shares its tabs and cookies.

`tab_close` and `context_close` mean more against an attached browser: they discard somebody's working state rather than scratch state. An adopted context is never disposed by the server, so its windows survive even when the session lets go of it.

A debugging port grants complete control of the browser and every session inside it. [Attaching to a Running Browser](attaching-to-a-running-browser.md) covers that in full, along with how to start a browser with the port open.

### Coordinate interaction

Some applications render their interface to a `<canvas>` or another custom surface, so the accessibility tree has nothing to address and ref-based tools have nothing to bind to. When that happens, `snapshot` says so explicitly and the coordinate tools take over. The workflow is perception by screenshot instead of by tree:

1. `screenshot` the page and identify the control visually.
2. Act on its position with `click_xy`, `hover_xy`, `scroll_xy`, or `drag`. Coordinates are CSS pixels in the viewport, the same space screenshots and `getBoundingClientRect()` report.
3. If a target sits at or beyond the viewport edge, `resize` the viewport first; the session default is 1280x800 and `--viewport` changes it at launch.

All coordinate input is dispatched as trusted browser-level events, exactly like the ref-based tools, so frameworks that ignore synthetic JavaScript events respond to it. The browser's own hit test decides the target at the point: an overlay with `pointer-events: none` is passed through automatically, while an overlay that accepts pointer events receives the event just as it would a real click.

`drag` accepts either elements (`start_ref`/`end_ref`, each a ref or a selector) or coordinates (`start_x`/`start_y`/`end_x`/`end_y`), one addressing mode per call, so it works on semantic DOM and canvas surfaces alike. Intermediate pointer moves are always emitted, with `steps` and `hold_ms` available for libraries that threshold or debounce drag starts.

### Video recording

`video_start` and `video_stop` record the active page to a video file, following the same start/stop convention as traces and HARs: stopping finalizes the file and returns its path, and an omitted path is auto-generated in the server's output directory. The capture runs at the viewport's resolution.

Two characteristics are inherent to the browser's screencast and worth knowing before scripting a session around it: frames are paced by screen updates rather than a fixed clock, and no mouse cursor appears in the footage, so recordings show the interface changing without a visible pointer. This suits verification and failure-record footage well; for presentation-grade recordings, capture the headed browser with a screen recorder instead. Launch the server with `--show-cursor` to draw a pseudo-cursor that follows the synthetic pointer and flashes on each click, which makes the action legible in screenshots and recordings.

The output container is MJPEG in AVI, written without external dependencies. Most editors and players open it directly; convert with ffmpeg (`ffmpeg -i in.avi -c:v libx264 out.mp4`) when another format is needed.

To record everything without per-page tool calls, launch the server with `--record-video <dir>`: every page records for its whole life and finalizes when it closes, one file per page. In that mode the on-demand tools report an error, since each page is already recording.

---

## Command options

`motus mcp` runs over stdio by default. The options below apply to both transports unless noted.

| Option | Default | Description |
|---|---|---|
| `--connect` | _(none)_ | Drive a browser that is already running instead of starting one. Takes the debugging endpoint it was started with (`http://127.0.0.1:9222`) or its CDP WebSocket URL. The browser is never closed by the server, and the options that describe starting one no longer apply. |
| `--headless` | `true` | Run the browser without a visible window. Pass `--headless false` to watch the agent drive a real window. |
| `--channel` | `chromium` | Browser to drive: `chromium`, `chrome`, `edge`, or `firefox`. A channel named here has to be installed: the server stops with an error rather than starting a different browser in its place. Snapshot refs need a Chromium-based browser, because the accessibility tree they are built from is read over the Chrome DevTools Protocol; on Firefox the `snapshot` tool says so rather than describing the page, and the coordinate tools (`click_xy`, `hover_xy`, `drag`, `scroll_xy`) still work against a screenshot. |
| `--executable-path` | _(none)_ | Start this browser binary instead of resolving one from `--channel`. Pins a session to an exact build, and is the way past a channel the server cannot find. |
| `--browser-arg` | _(none)_ | An extra command-line argument for the browser. Attach the value with `=`, and repeat the flag for more than one: `--browser-arg=--no-sandbox --browser-arg=--disable-dev-shm-usage`. Chromium refuses to start as root, so a container usually needs `--no-sandbox`. |
| `--user-data-dir` | _(none)_ | Browser profile directory. Cookies, history, and signed-in sessions persist in it between runs instead of every session starting clean. |
| `--storage-state` | _(none)_ | Seed every context with the cookies and local storage saved in this file, as written by `IBrowserContext.StorageStateAsync`. |
| `--viewport` | `1280x800` | Viewport size for every page, as `WIDTHxHEIGHT`. The `resize` tool changes it per page at runtime. |
| `--user-agent` | _(browser default)_ | User agent string every page reports. |
| `--locale` | _(browser default)_ | Locale every page formats dates, numbers, and sorted text with, such as `en-GB`. |
| `--timezone` | _(machine default)_ | Time zone every page reports, such as `Europe/Berlin`. |
| `--proxy-server` | _(none)_ | Send browser traffic through this proxy, such as `http://127.0.0.1:8080`. |
| `--proxy-bypass` | _(none)_ | Comma-separated hosts that skip the proxy, such as `localhost,*.internal`. Needs `--proxy-server`. |
| `--timeout` | _(framework default)_ | How long an element action waits for its target, in milliseconds. Applies to every ref-addressed tool. |
| `--navigation-timeout` | _(framework default)_ | How long a navigation waits to finish, in milliseconds. Applies to `navigate`, `reload`, `go_back`, `go_forward`, and `tab_open`. |
| `--settle` | `500` | How long an action waits, after the browser accepts it, for the page to show what it did before the result is written, in milliseconds. Every action pays it, so keep it short; `0` describes the page the instant the action returns. |
| `--dialogs` | `ask` | What becomes of a JavaScript dialog the page raises: `accept` answers it, `dismiss` cancels it, and `ask` leaves it pending for `handle_dialog`. |
| `--record-video` | _(none)_ | Record a video of every page into this directory, one MJPEG AVI per page, finalized when the page closes. |
| `--show-cursor` | `false` | Draw an on-screen pseudo-cursor in screenshots and recordings. It follows the element's CSS cursor style and shows a click effect. Enables `--natural-mouse` unless that is set explicitly. |
| `--natural-mouse` | `--show-cursor` | Move the mouse along a curved, eased path instead of jumping to the target, so motion looks human and the page receives a realistic event stream. Pass `--natural-mouse false` to keep the cursor without it. Adds latency to every move. |
| `--config` | _(none)_ | Read defaults from this `motus.config.json` file, which is how a suite's settings are reused instead of restated. It fills in `--headless`, `--channel`, `--executable-path`, `--viewport`, `--locale`, and `--timeout`; anything given on the command line wins over it. |
| `--http` | `false` | Serve over Streamable HTTP for concurrent remote clients instead of stdio. |
| `--host` | `127.0.0.1` | Interface to bind when `--http` is set. |
| `--port` | `8931` | TCP port to listen on when `--http` is set. |
| `--token` | _(none)_ | Bearer token required on every HTTP request. May also be supplied via the `MOTUS_MCP_TOKEN` environment variable. Required when binding a non-loopback host. |
| `--output-dir` | _(a directory for this run under the temporary directory)_ | Directory that the tools writing a file resolve their paths inside. Printed to standard error at startup. |
| `--allow-unrestricted-file-access` | `false` | Let tools read and write anywhere on the machine, and open `file://` URLs. |
| `--allow-attach` | `false` | Let `browser_attach` point the session at a browser that is already running. Implied by `--connect`. |

---

## Recovery after a browser crash

Each session holds one browser. If its process crashes or stops responding (for example, a renderer abort on a heavy WebGL or canvas page), the next tool call that touches the browser disposes the dead instance, launches a fresh one in its place, and proceeds. The one call that raced the crash returns an error; the call after it recovers on its own, so a transient browser failure does not require restarting the server or reconnecting the client.

A relaunched browser starts clean. Open tabs and named contexts do not carry over, and the refs from the last `snapshot` no longer resolve, so `navigate` and take a fresh `snapshot` before addressing elements again.

---

## HTTP transport

For hosted, team, or CI use, the same tools can be served over Streamable HTTP instead of stdio. This is the transport to use with remote MCP clients and the Anthropic API MCP connector.

```bash
# Loopback only, no token required
motus mcp --http

# Reachable from other machines: a non-loopback bind requires a token
motus mcp --http --host 0.0.0.0 --port 8931 --token "$MOTUS_MCP_TOKEN"
```

Each connected client gets its own isolated browser session; sessions and the browsers they hold are reaped after a period of inactivity. Security stays deliberately minimal: the server binds the loopback interface by default, and binding any non-loopback host without a token is refused at startup. When a token is configured, every request is checked against it with a constant-time comparison.

stdio inherits the trust of the local user that launched it and needs no token. HTTP does not, so treat the token as a credential and prefer loopback or a trusted network.

### Security defaults

An agent driving this server acts partly on instructions that came from the pages it visited, so the server keeps four boundaries whichever transport it is serving.

- **Writes land in one directory.** `trace_stop`, `har_stop` and `video_start` resolve the path they are given inside the output directory, which is a directory for this run under the system temporary directory unless `--output-dir` names another. The directory is printed to standard error at startup, and every result that writes a file echoes the absolute path it wrote, so an artifact is always findable. An absolute path, a path that climbs out with `..`, and a path that follows a symbolic link out are each refused.
- **Reads come from the client's roots.** `upload_files` reads a file only from inside one of the roots the MCP client reported for the session. A client that reports none leaves the server's own working directory and the output directory.
- **`file://` is blocked.** `navigate`, `tab_open`, and the route tools refuse a `file:` URL, including one reached through a redirect a mock sets up. Without this, a page could talk an agent into reading the machine's files back through a snapshot.
- **Attaching needs an option.** `browser_attach` refuses unless the server was started with `--allow-attach` or `--connect`. The tool stays listed either way, so an agent that needs it can say what to restart with rather than reporting a capability Motus does not have.

`--allow-unrestricted-file-access` lifts the first three together. `--allow-attach` lifts the fourth.

---

## What's next

- [Recording and Code Generation](recording-and-codegen.md) -- the `generate_pom` tool builds on the same codegen engine
- [Accessibility Testing](accessibility-testing.md) -- the `audit_accessibility` tool runs the same WCAG rules
- [Performance Testing](performance-testing.md) -- the `get_performance` tool collects the same Core Web Vitals
- [Network Interception](network-interception.md) -- the route tools expose the same interception engine
- [Attaching to a Running Browser](attaching-to-a-running-browser.md) -- `--connect` and `browser_attach` in full, including the security note
- [Frames and iframes](frames-and-iframes.md) -- what `frame_select` scopes, and the two traps worth knowing
