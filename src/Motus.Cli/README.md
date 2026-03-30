# Motus.Cli

Command-line tool for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework. Run tests, record interactions, manage browser installations, and inspect traces from your terminal.

## Installation

```bash
# Global install
dotnet tool install -g Motus.Cli

# Local install (per-project)
dotnet tool install Motus.Cli

# Update
dotnet tool update -g Motus.Cli
```

Requires .NET 8.0 SDK or later.

## Commands

### `motus run`

Discovers and runs tests from compiled assemblies.

```bash
# Run all tests
motus run MyTests.dll

# Filter and parallelize
motus run MyTests.dll --filter "Category=Smoke" --workers 4

# Output JUnit XML for CI
motus run MyTests.dll --reporter junit --output results.xml
```

| Option | Default | Description |
|--------|---------|-------------|
| `[assemblies]` | none | One or more test assembly paths |
| `--filter` | none | Test filter expression |
| `--reporter` | console | Reporter: `console`, `junit`, `html`, `trx` |
| `--output` | none | Output file path for the reporter |
| `--workers` | auto | Number of parallel workers (auto = processor count) |
| `--visual` | false | Launch the visual runner on port 5100 |
| `--verbose` | false | Show detailed ASP.NET Core log output (visual runner) |

### `motus record`

Launches a headed browser, records interactions, and emits C# test code.

```bash
# Record a session
motus record --url https://example.com --output LoginTest.cs

# Connect to an existing browser
motus record --connect ws://localhost:9222 --framework xunit
```

| Option | Default | Description |
|--------|---------|-------------|
| `--url` | none | Starting URL to navigate to |
| `--output` | none | Output file path for generated code |
| `--framework` | mstest | Target test framework: `mstest`, `xunit`, `nunit` |
| `--connect` | none | WebSocket endpoint of an existing browser |
| `--class-name` | RecordedTest | Generated test class name |
| `--method-name` | Test | Generated test method name |
| `--namespace` | RecordedTests | Generated namespace |
| `--preserve-timing` | false | Include delays between actions |

### `motus install`

Downloads and installs a browser binary.

```bash
# Install Chromium
motus install chromium

# Install a specific revision
motus install chrome --revision 1234567

# Install to a custom path
motus install firefox --path ./browsers
```

| Option | Default | Description |
|--------|---------|-------------|
| `--channel` | chromium | Browser: `chromium`, `chrome`, `edge`, `firefox` |
| `--revision` | latest | Specific browser revision to install |
| `--path` | default | Custom installation directory |

### `motus trace show`

Opens a recorded trace in the visual runner. The trace viewer displays a timeline of browser events with timestamps, durations, screenshots, and network requests extracted from the trace ZIP.

```bash
motus trace show trace.zip --port 5200
```

| Option | Default | Description |
|--------|---------|-------------|
| `[file]` | none | Path to a trace ZIP file |
| `--port` | 5200 | Port for the trace viewer |

### `motus screenshot`

Captures a screenshot from a URL.

```bash
# Basic capture
motus screenshot https://example.com --output page.png

# Full page capture at a specific viewport size
motus screenshot https://example.com --output page.png --full-page --width 1920 --height 1080

# Wait for JS-heavy sites and remove cookie banners
motus screenshot https://example.com --output page.png --delay 5 --hide-banners
```

| Option | Default | Description |
|--------|---------|-------------|
| `[url]` | none | URL to capture |
| `--output` | screenshot.png | Output file path |
| `--full-page` | false | Capture the full scrollable page |
| `--width` | 1280 | Viewport width in pixels |
| `--height` | 720 | Viewport height in pixels |
| `--timeout` | 60 | Navigation timeout in seconds |
| `--wait-until` | Load | Wait condition: `Load`, `DOMContentLoaded`, `NetworkIdle` |
| `--delay` | 0 | Seconds to wait after navigation before capture |
| `--hide-banners` | false | Remove cookie consent and privacy banners before capture |

### `motus pdf`

Generates a PDF from a URL.

```bash
# Basic PDF
motus pdf https://example.com --output page.pdf

# Wait for client-side rendering and remove banners
motus pdf https://example.com --output page.pdf --delay 5 --hide-banners --width 1440
```

| Option | Default | Description |
|--------|---------|-------------|
| `[url]` | none | URL to render as PDF |
| `--output` | output.pdf | Output file path |
| `--timeout` | 60 | Navigation timeout in seconds |
| `--wait-until` | Load | Wait condition: `Load`, `DOMContentLoaded`, `NetworkIdle` |
| `--width` | 1440 | Viewport width in pixels |
| `--delay` | 0 | Seconds to wait after navigation before capture |
| `--hide-banners` | false | Remove cookie consent and privacy banners before capture |

### `motus codegen`

Generates Page Object Model classes from live web pages by crawling the DOM and inferring selectors.

```bash
# Generate from a URL (headless)
motus codegen https://example.com/login --output ./Pages --namespace MyApp.Pages

# Open a browser, navigate yourself, then press Enter to analyze
motus codegen --headed --output ./Pages

# Navigate to a URL in a visible browser, interact, then press Enter
motus codegen https://example.com/login --headed --output ./Pages

# Connect to an already-running browser's active tab
motus codegen --connect ws://localhost:9222 --output ./Pages

# Only analyze elements inside a modal or specific container
motus codegen --headed --scope ".modal-dialog" --output ./Pages
motus codegen https://example.com/login --scope "#login-form" --output ./Pages
```

| Option | Default | Description |
|--------|---------|-------------|
| `[url]` | none | One or more URLs (optional with `--headed` or `--connect`) |
| `--output` | . | Output directory for generated files |
| `--namespace` | Motus.Generated | Namespace for generated classes |
| `--headed` | false | Launch a visible browser for interactive navigation before analysis |
| `--connect` | none | WebSocket endpoint to attach to a running browser |
| `--scope` | none | CSS selector to limit discovery to a container (e.g. `".modal"`, `"#form"`) |
| `--selector-priority` | none | Comma-separated strategy priority (e.g. `testid,role,text,css`) |
| `--timeout` | 30000 | Navigation timeout in milliseconds |
| `--detect-listeners` | false | Detect elements with JS event listeners |

### `motus update-protocol`

Updates the bundled CDP protocol JSON files to the latest version.
