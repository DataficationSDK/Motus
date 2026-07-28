# Motus.Cli

Command-line tool for the [Motus](https://github.com/DataficationSDK/Motus) browser automation framework. Run tests, record interactions, generate page objects, manage browser installations, inspect traces, and serve the MCP server from your terminal.

## Installation

```bash
# Global install
dotnet tool install -g Motus.Cli

# Local install (per-project)
dotnet tool install Motus.Cli

# Update
dotnet tool update -g Motus.Cli
```

Requires .NET 8.0 SDK or later. Run `motus install` once so a browser is available.

## Commands

| Command | Purpose |
|---|---|
| `motus run` | Discover and execute tests from compiled assemblies |
| `motus record` | Record browser interactions and emit test code |
| `motus codegen` | Generate Page Object Model classes from live pages |
| `motus install` | Download and install a browser |
| `motus screenshot` | Capture a screenshot of a page |
| `motus pdf` | Render a page to PDF |
| `motus trace show` | Open a recorded trace in the viewer |
| `motus trx show` | Open a TRX result file in the viewer |
| `motus shard merge` | Merge per-shard result files into one report |
| `motus check-selectors` | Validate recorded selectors against live pages |
| `motus mcp` | Run the MCP server for agent clients |
| `motus update-protocol` | Refresh the bundled CDP protocol definitions |

Run `motus <command> --help` for the options of any one of them.

## Common tasks

```bash
# Install a browser, pinned to an exact revision for reproducible CI
motus install
motus install --channel chrome --revision 1421000

# Run a suite, filtered, with four workers
motus run bin/Release/net8.0/MyTests.dll --filter Checkout --workers 4

# Console output plus a JUnit file for CI. The path is part of the value.
motus run bin/Release/net8.0/MyTests.dll --reporter console --reporter junit:results.xml

# Split across four agents, then merge. --expect catches a shard that never ran.
motus run bin/Release/net8.0/MyTests.dll --shard 1/4 --reporter junit:results.shard-1.xml
motus shard merge results.shard-*.xml --output junit:results.xml --expect 4

# Retry a test that lost its browser, and label anything that only passes on a retry
motus run bin/Release/net8.0/MyTests.dll --retries 2 --retry-policy flake --fail-on-flaky

# Collect JS and CSS coverage; repeat the flag for multiple formats
motus run bin/Release/net8.0/MyTests.dll --coverage console --coverage html:./coverage

# Record a session into a test, or generate page objects from a live page
motus record --url https://example.com --output LoginTest.cs
motus codegen https://example.com/login --output ./Pages --namespace MyApp.Pages

# Capture a page
motus screenshot https://example.com --output page.png --full-page
motus pdf https://example.com --output page.pdf --delay 5 --hide-banners

# Register the MCP server with an agent, or point it at a browser already running
claude mcp add motus -- motus mcp
motus mcp --connect http://127.0.0.1:9222
```

## Full reference

Every command, option, default and exit code is documented at
**[motustesting.com/docs/reference/cli.html](https://motustesting.com/docs/reference/cli.html)**.

See also the [MCP Server guide](https://motustesting.com/docs/guides/mcp-server.html) for the tool catalog,
[Sharding](https://motustesting.com/docs/guides/sharding.html),
[Flaky Tests and Quarantine](https://motustesting.com/docs/guides/flaky-tests-and-quarantine.html),
and [Configuration](https://motustesting.com/docs/guides/configuration.html) for the settings behind many of these options.
