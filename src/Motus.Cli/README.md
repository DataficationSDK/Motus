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

Opens a recorded trace in the visual runner.

```bash
motus trace show trace.zip --port 5200
```

### `motus screenshot`

Captures a screenshot from a URL.

```bash
motus screenshot https://example.com --output page.png
```

### `motus pdf`

Generates a PDF from a URL.

```bash
motus pdf https://example.com --output page.pdf
```

### `motus codegen`

Source code generation utilities.

### `motus update-protocol`

Updates the bundled CDP protocol JSON files to the latest version.
