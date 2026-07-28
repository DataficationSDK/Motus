# CLI Reference

Every command the `motus` tool exposes, with its arguments, options and defaults.

```bash
dotnet tool install -g Motus.Cli
motus --help
```

The tool targets .NET 8.0 or later. Most commands need a browser, which `motus install` provides.

| Command | Purpose |
|---|---|
| [`motus run`](#motus-run) | Discover and execute tests from compiled assemblies |
| [`motus record`](#motus-record) | Record browser interactions and emit test code |
| [`motus codegen`](#motus-codegen) | Generate Page Object Model classes from live pages |
| [`motus install`](#motus-install) | Download and install a browser |
| [`motus screenshot`](#motus-screenshot) | Capture a screenshot of a page |
| [`motus pdf`](#motus-pdf) | Render a page to PDF |
| [`motus trace show`](#motus-trace-show) | Open a recorded trace in the viewer |
| [`motus trx show`](#motus-trx-show) | Open a TRX result file in the viewer |
| [`motus shard merge`](#motus-shard-merge) | Merge per-shard result files into one report |
| [`motus check-selectors`](#motus-check-selectors) | Validate recorded selectors against live pages |
| [`motus mcp`](#motus-mcp) | Run the MCP server for agent clients |
| [`motus update-protocol`](#motus-update-protocol) | Refresh the bundled CDP protocol definitions |

---

## `motus run`

Discovers tests in compiled assemblies and executes them.

```bash
# Run everything in an assembly
motus run bin/Release/net8.0/MyTests.dll

# Filter by name, and use four workers
motus run bin/Release/net8.0/MyTests.dll --filter Checkout --workers 4

# Write a JUnit file for CI as well as the console table
motus run bin/Release/net8.0/MyTests.dll --reporter console --reporter junit:results.xml

# One shard of four, retrying a lost browser twice
motus run bin/Release/net8.0/MyTests.dll --shard 1/4 --retries 2
```

| Option | Default | Description |
|---|---|---|
| `[assemblies]` | none | One or more test assembly paths. |
| `--filter` | none | Keep only tests whose fully qualified name contains this substring. Case-insensitive. |
| `--reporter` | `console` | Output format: `console`, `junit:<path>`, `html:<path>`, `trx:<path>`. Repeat for more than one. |
| `--workers` | `auto` | Parallel workers. `auto` uses the processor count. |
| `--visual` | `false` | Launch the visual runner on port 5100 instead of running in the terminal. |
| `--a11y` | none | Run accessibility audits: `warn` reports violations, `enforce` also fails tests with error-severity violations. |
| `--perf-budget` | `false` | Enforce the performance budget from configuration. |
| `--coverage` | none | Collect coverage and report it: `console`, `html:<dir>`, `cobertura:<path>`. Repeat for more than one. |
| `--retries` | `0` | Extra attempts for a failing test. |
| `--retry-policy` | `transient` | Which failures `--retries` re-runs: `transient` for a lost browser only, `flake` for any failure. |
| `--fail-on-flaky` | `false` | Exit non-zero when any test is flaky. By default a flaky test passes with a warning. |
| `--quarantine` | none | Path to a quarantine list file. Listed tests run and report but do not gate the run. |
| `--flaky-history` | none | Path to a JSON file accumulating per-test run, failure and flaky-pass counts. |
| `--shard` | none | Run one shard: `<index>/<total>`, 1-based, for example `1/4`. |

The output path for a reporter is part of its value, so it is `--reporter junit:results.xml` rather than a separate flag. Passing `--reporter junit` with no path writes nowhere.

**Exit code.** `0` when everything passed. `1` when any test failed, when a coverage threshold was missed, or when `--fail-on-flaky` was given and any test was flaky.

See [Sharding](../guides/sharding.md), [Flaky Tests and Quarantine](../guides/flaky-tests-and-quarantine.md), and [Configuration](../guides/configuration.md) for the settings behind these.

---

## `motus record`

Launches a headed browser, records what you do, and writes it out as test code.

```bash
motus record --url https://example.com --output LoginTest.cs
motus record --connect ws://localhost:9222 --framework xunit
```

| Option | Default | Description |
|---|---|---|
| `--url` | none | Starting URL to navigate to. |
| `--output` | `recorded-test.cs` | Output file path for the generated code. |
| `--framework` | `mstest` | Target framework: `mstest`, `xunit`, `nunit`. |
| `--connect` | none | WebSocket endpoint of an already-running browser. |
| `--class-name` | `RecordedTest` | Generated class name. |
| `--method-name` | `RecordedScenario` | Generated method name. |
| `--namespace` | `Motus.Generated` | Generated namespace. |
| `--preserve-timing` | `false` | Emit delays between actions matching the original pace. |
| `--width` | `1024` | Viewport width in pixels. |
| `--height` | `768` | Viewport height in pixels. |
| `--selector-priority` | none | Reserved. Accepted but not yet applied to recording. |

---

## `motus codegen`

Crawls a page and generates Page Object Model classes from what it finds.

```bash
# Straight from a URL
motus codegen https://example.com/login --output ./Pages --namespace MyApp.Pages

# Open a browser, navigate and sign in yourself, then press Enter to analyze
motus codegen --headed --output ./Pages

# Only look inside a dialog
motus codegen https://example.com/login --scope "#login-form" --output ./Pages
```

| Option | Default | Description |
|---|---|---|
| `[url]` | none | One or more URLs. Optional when `--headed` or `--connect` is used. |
| `--output` | `.` | Output directory for generated files. |
| `--namespace` | `Motus.Generated` | Namespace for generated classes. |
| `--headed` | `false` | Launch a visible browser so you can navigate before analysis. |
| `--connect` | none | WebSocket endpoint of an already-running browser, for example `ws://localhost:9222`. |
| `--scope` | none | CSS selector limiting discovery to one container, for example `".modal-dialog"`. |
| `--selector-priority` | none | Comma-separated strategy order, for example `testid,role,text,css`. |
| `--timeout` | `30000` | Navigation timeout in milliseconds. |
| `--detect-listeners` | `false` | Also treat elements carrying JavaScript event listeners as interesting. |

---

## `motus install`

Downloads a browser into the local cache.

```bash
motus install
motus install --channel chrome
motus install --revision 1421000
```

| Option | Default | Description |
|---|---|---|
| `--channel` | `chromium` | Browser to install: `chromium`, `chrome`, `edge`, `firefox`. |
| `--revision` | latest | Install one exact revision instead of resolving the current one. |
| `--path` | default cache | Override the browser cache directory. |

Pinning a revision is what makes a CI run reproducible when the stable channel moves. The command prints the path it installed to, which is the value to hand to `launch.executablePath` or `MOTUS_EXECUTABLE_PATH`. See [Browser Lifecycle](../architecture/browser-lifecycle.md).

---

## `motus screenshot`

Captures a page to an image.

```bash
motus screenshot https://example.com --output page.png
motus screenshot https://example.com --output page.png --full-page --width 1920 --height 1080
motus screenshot https://example.com --output page.png --delay 5 --hide-banners
```

| Option | Default | Description |
|---|---|---|
| `[url]` | none | URL to capture. |
| `--output` | `screenshot.png` | Output file path. |
| `--full-page` | `false` | Capture the whole scrollable page rather than the viewport. |
| `--width` | `1280` | Viewport width in pixels. |
| `--height` | `720` | Viewport height in pixels. |
| `--timeout` | `60` | Navigation timeout in seconds. |
| `--wait-until` | `Load` | Wait condition: `Load`, `DOMContentLoaded`, `NetworkIdle`. |
| `--delay` | `0` | Seconds to wait after navigation before capturing. |
| `--hide-banners` | `false` | Remove cookie consent and privacy banners first. |

---

## `motus pdf`

Renders a page to PDF.

```bash
motus pdf https://example.com --output page.pdf
motus pdf https://example.com --output page.pdf --delay 5 --hide-banners --width 1440
```

| Option | Default | Description |
|---|---|---|
| `[url]` | none | URL to render. |
| `--output` | `output.pdf` | Output file path. |
| `--timeout` | `60` | Navigation timeout in seconds. |
| `--wait-until` | `Load` | Wait condition: `Load`, `DOMContentLoaded`, `NetworkIdle`. |
| `--width` | `1440` | Viewport width in pixels. |
| `--delay` | `0` | Seconds to wait after navigation before rendering. |
| `--hide-banners` | `false` | Remove cookie consent and privacy banners first. |

---

## `motus trace show`

Opens a trace ZIP in the visual runner, showing a timeline of events with screenshots and network activity.

```bash
motus trace show trace.zip --port 5200
```

| Option | Default | Description |
|---|---|---|
| `[file]` | none | Path to a trace ZIP file. |
| `--port` | `5200` | Port for the viewer. |

---

## `motus trx show`

Opens a TRX result file in the visual runner.

```bash
motus trx show results.trx --port 5300
```

| Option | Default | Description |
|---|---|---|
| `[file]` | none | Path to a `.trx` result file. |
| `--port` | `5300` | Port for the viewer. |

The path must exist and end in `.trx`.

---

## `motus shard merge`

Reads the result files written by each shard and combines them into one report.

```bash
motus shard merge results.shard-*.xml --output junit:results.xml --expect 4
```

| Option | Default | Description |
|---|---|---|
| `[files]` | none | One or more per-shard result files, JUnit or TRX. A glob is accepted if the shell did not expand it. |
| `--output` | none | Write the merged report: `junit:<path>` or `trx:<path>`. Repeat for more than one. Omit for a console summary only. |
| `--expect` | none | Assert that exactly this many shards are present, read from the coordinates each shard stamped into its file. |

**Use `--expect` in CI.** Without it, a shard whose agent died before writing its file is simply absent, and the merge reports a smaller, entirely green suite. That failure is invisible precisely when it matters. See [Sharding](../guides/sharding.md).

**Exit code.** `0` when every shard was present, the merge validated, and no test failed. `1` otherwise.

---

## `motus check-selectors`

Scans C# sources for recorded selectors and checks each one against a live page.

```bash
motus check-selectors "./Tests/**/*.cs" --base-url https://staging.example.com --ci
motus check-selectors "./Tests/**/*.cs" --manifest Login.selectors.json --fix
```

| Option | Default | Description |
|---|---|---|
| `[glob]` | none | Glob for the C# files to scan, for example `"./Tests/**/*.cs"`. |
| `--manifest` | none | Path to a `*.selectors.json` manifest. Selectors are then checked against the page URL each was recorded on. |
| `--base-url` | none | Check every selector against this one URL. Required when `--manifest` is not given. |
| `--ci` | `false` | Exit non-zero if any selector is broken. |
| `--json` | none | Write full results as JSON to this path. |
| `--fix` | `false` | Apply high-confidence repairs to the source files. |
| `--no-backup` | `false` | Skip writing `.bak` files when applying repairs. |
| `--interactive` | `false` | Review and apply repairs one selector at a time in the visual runner. |

Repairs need a fingerprint match, which only a manifest carries, so `--fix` and `--interactive` both require `--manifest`, and the two cannot be combined with each other. A usage error exits `2`.

---

## `motus mcp`

Runs the Model Context Protocol server so an agent can drive a browser through Motus. Serves over stdio by default.

```bash
# Register with Claude Code
claude mcp add motus -- motus mcp

# Show a window, and record what happens
motus mcp --headless false --record-video ./videos --show-cursor

# Drive a browser that is already running
motus mcp --connect http://127.0.0.1:9222

# Serve over Streamable HTTP; a non-loopback bind requires a token
motus mcp --http --host 0.0.0.0 --port 8931 --token "$MOTUS_MCP_TOKEN"
```

| Option | Default | Description |
|---|---|---|
| `--headless` | `true` | Run the browser without a visible window. |
| `--channel` | `chromium` | Browser to drive: `chromium`, `chrome`, `edge`, `firefox`. |
| `--connect` | none | Drive a browser that is already running, given its debugging endpoint (`http://127.0.0.1:9222`) or CDP WebSocket URL. The server never closes it. |
| `--viewport` | `1280x800` | Viewport for every page, as `WIDTHxHEIGHT`. |
| `--record-video` | none | Record every page into this directory, one MJPEG AVI per page. |
| `--show-cursor` | `false` | Draw an on-screen pointer and click effects into the page so captures show them. Turns on natural mouse motion unless `--natural-mouse` says otherwise. |
| `--natural-mouse` | follows `--show-cursor` | Move along curved, eased paths. Pass `--natural-mouse false` to keep the cursor without it. |
| `--http` | `false` | Serve over Streamable HTTP instead of stdio. |
| `--host` | `127.0.0.1` | Interface to bind when `--http` is set. |
| `--port` | `8931` | Port to listen on when `--http` is set. |
| `--token` | none | Bearer token required on every HTTP request, or set `MOTUS_MCP_TOKEN`. Required for a non-loopback bind. |

`--connect` adopts a browser the server did not start, so the options that describe how to start one (`--headless`, `--channel`) and how to build a context (`--viewport`, `--record-video`, `--show-cursor`, `--natural-mouse`) no longer apply. Passing them alongside `--connect` reports which were ignored rather than silently dropping them.

Combining `--connect` with `--http` is allowed but warned about: HTTP mode otherwise gives each client its own isolated browser, and a shared endpoint points every session at the same one, so clients share tabs and cookies.

See [MCP Server](../guides/mcp-server.md) for the tool catalog, and [Attaching to a Running Browser](../guides/attaching-to-a-running-browser.md) for the security implications of an open debugging port.

---

## `motus update-protocol`

Downloads the current Chrome DevTools Protocol definitions and updates the bundled copies.

| Option | Default | Description |
|---|---|---|
| `--version` | latest | Protocol version to fetch. |
| `--dry-run` | `false` | Show the diff without writing files. |
| `--output-dir` | `.` | Directory to write protocol files into. |

---

## See Also

- [Configuration](../guides/configuration.md) -- the `motus.config.json` schema and environment variables behind many of these options
- [Sharding](../guides/sharding.md) -- `--shard` and `shard merge` in context
- [Flaky Tests and Quarantine](../guides/flaky-tests-and-quarantine.md) -- `--retries`, `--retry-policy`, `--quarantine` and `--flaky-history`
- [MCP Server](../guides/mcp-server.md) -- the agent-facing tool catalog
