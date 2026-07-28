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

The server responds with an `initialize` result identifying itself as `motus`, followed by a `tools/list` result enumerating every available tool. A navigate-then-snapshot exchange exercises a live browser end to end: send a `tools/call` for `navigate` with a `url`, wait for the browser to launch, then a `tools/call` for `snapshot`. The snapshot result is an indented accessibility tree with a `[ref=...]` on each node, which is what an agent uses to target elements.

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
| Network | `route_fulfill`, `route_abort`, `route_continue`, `unroute`, `route_list`, `network_requests` |
| Console | `console_messages` |
| Recording and codegen | `generate_pom`, `trace_start`, `trace_stop`, `har_start`, `har_stop`, `video_start`, `video_stop` |

Elements are addressed by the `ref` values returned in a snapshot rather than by CSS or XPath. Take a `snapshot`, then pass a node's `ref` to `click`, `type`, or another interaction tool. References are relative to the most recent snapshot, so take a fresh snapshot after the page changes.

### Reading a value with `evaluate`

`evaluate` returns whatever the expression produced under a `result` key:

```
evaluate("document.querySelectorAll('article').length")
  -> {"result": 12}
```

Structured content has to be a JSON object, so an expression returning a bare number, string, or array could not be sent back as-is. Wrapping every value in the same shape means an expression may return anything: reading a single count off the page works exactly as readily as returning a record. A value that cannot be serialized, such as `undefined`, a function, or a DOM node, comes back as `{"result": null}`.

Read the value at `result`. Wrapping the expression by hand, as `({ count: ... })`, is no longer necessary, though it remains harmless and simply nests one level deeper.

### Frames

A page snapshot describes each `iframe` element but not what is inside it, and for a frame the browser renders in its own process the contents are not in the page's tree at all. Frames are addressed by selection, the same way tabs and contexts are:

1. `frame_list` lists the frames in document order with their nesting depth. Index 0 is the page itself.
2. `frame_select <index>` scopes the session to one of them. `frame_select 0` returns to the page.
3. `snapshot` then describes that frame, and its refs address elements inside it.

Scope covers `snapshot`, `evaluate`, and the `wait_for` text conditions. Every interaction tool is already covered through refs: a ref from a scoped snapshot keeps addressing the frame it came from, even after the scope moves on. Selection resets on navigation and on switching tab or context, since the frame it named is gone by then.

The coordinate tools stay in page coordinates whatever is selected. Their input is dispatched at the page level and the browser decides which frame is under the point.

A page snapshot says how many frames its tree does not describe, so an agent that finds an `iframe` with nothing under it is pointed at `frame_list` rather than left to conclude the content is missing. See [Frames and iframes](frames-and-iframes.md).

### Driving a browser that is already running

By default the server starts a browser and ends it on shutdown. Pointed at a browser somebody else started, it drives what is already open in that browser and leaves it running afterwards. This is how a signed-in profile, a warm browser reused between runs, or a Chromium-based desktop application becomes drivable.

```bash
motus mcp --connect http://127.0.0.1:9222
```

An agent can also attach at any point with `browser_attach`, which is what to reach for when the endpoint is not known at the time the MCP client is configured. `browser_status` reports which browser is being driven and whether the server started it. Attaching closes the browser the server started, if any, and drops snapshot refs, route rules, and captured console output, so take a fresh snapshot afterwards.

Two consequences are worth knowing:

- **Options that describe starting a browser have nothing to act on.** `--headless`, `--channel`, `--viewport`, `--record-video` and `--show-cursor` bind either at launch or at context creation, and an attached session does neither: it adopts the context the browser is already using. The server says so on startup rather than ignoring them silently. The `resize` tool still changes a page's viewport at runtime.
- **`--http` with `--connect` means clients share one browser.** The HTTP transport otherwise gives each connected client its own isolated browser. Pointed at one endpoint, every session drives the same browser, and so shares its tabs and cookies.

`tab_close` and `context_close` mean more against an attached browser: they discard somebody's working state rather than scratch state. An adopted context is never disposed by the server, so its windows survive even when the session lets go of it.

A debugging port grants complete control of the browser and every session inside it. [Attaching to a Running Browser](attaching-to-a-running-browser.md) covers that in full, along with how to start a browser with the port open.

### Coordinate interaction

Some applications render their interface to a `<canvas>` or another custom surface, so the accessibility tree has nothing to address and ref-based tools have nothing to bind to. When that happens, `snapshot` says so explicitly and the coordinate tools take over. The workflow is perception by screenshot instead of by tree:

1. `screenshot` the page and identify the control visually.
2. Act on its position with `click_xy`, `hover_xy`, `scroll_xy`, or `drag`. Coordinates are CSS pixels in the viewport, the same space screenshots and `getBoundingClientRect()` report.
3. If a target sits at or beyond the viewport edge, `resize` the viewport first; the session default is 1280x800 and `--viewport` changes it at launch.

All coordinate input is dispatched as trusted browser-level events, exactly like the ref-based tools, so frameworks that ignore synthetic JavaScript events respond to it. The browser's own hit test decides the target at the point: an overlay with `pointer-events: none` is passed through automatically, while an overlay that accepts pointer events receives the event just as it would a real click.

`drag` accepts either refs (`start_ref`/`end_ref`) or coordinates (`start_x`/`start_y`/`end_x`/`end_y`), one addressing mode per call, so it works on semantic DOM and canvas surfaces alike. Intermediate pointer moves are always emitted, with `steps` and `hold_ms` available for libraries that threshold or debounce drag starts.

### Video recording

`video_start` and `video_stop` record the active page to a video file, following the same start/stop convention as traces and HARs: stopping finalizes the file and returns its path, and an omitted path is auto-generated under the temporary directory. The capture runs at the viewport's resolution.

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
| `--channel` | `chromium` | Browser to drive: `chromium`, `chrome`, `edge`, or `firefox`. |
| `--viewport` | `1280x800` | Viewport size for every page, as `WIDTHxHEIGHT`. The `resize` tool changes it per page at runtime. |
| `--record-video` | _(none)_ | Record a video of every page into this directory, one MJPEG AVI per page, finalized when the page closes. |
| `--show-cursor` | `false` | Draw an on-screen pseudo-cursor in screenshots and recordings. It follows the element's CSS cursor style and shows a click effect. Enables `--natural-mouse` unless that is set explicitly. |
| `--natural-mouse` | `--show-cursor` | Move the mouse along a curved, eased path instead of jumping to the target, so motion looks human and the page receives a realistic event stream. Pass `--natural-mouse false` to keep the cursor without it. Adds latency to every move. |
| `--http` | `false` | Serve over Streamable HTTP for concurrent remote clients instead of stdio. |
| `--host` | `127.0.0.1` | Interface to bind when `--http` is set. |
| `--port` | `8931` | TCP port to listen on when `--http` is set. |
| `--token` | _(none)_ | Bearer token required on every HTTP request. May also be supplied via the `MOTUS_MCP_TOKEN` environment variable. Required when binding a non-loopback host. |

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

---

## What's next

- [Recording and Code Generation](recording-and-codegen.md) -- the `generate_pom` tool builds on the same codegen engine
- [Accessibility Testing](accessibility-testing.md) -- the `audit_accessibility` tool runs the same WCAG rules
- [Performance Testing](performance-testing.md) -- the `get_performance` tool collects the same Core Web Vitals
- [Network Interception](network-interception.md) -- the route tools expose the same interception engine
- [Attaching to a Running Browser](attaching-to-a-running-browser.md) -- `--connect` and `browser_attach` in full, including the security note
- [Frames and iframes](frames-and-iframes.md) -- what `frame_select` scopes, and the two traps worth knowing
