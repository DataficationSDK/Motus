using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.Json;
using Motus.Abstractions;
using Motus.Mcp;
using Motus.Mcp.Http;

namespace Motus.Cli.Commands;

public static class McpCommand
{
    private const string TokenEnvVar = "MOTUS_MCP_TOKEN";

    /// <summary>
    /// The environment variable that names tool groups, as a comma-separated list. An MCP client's
    /// launch configuration is often easier to set a variable in than to edit an argument list.
    /// </summary>
    private const string CapsEnvVar = "MOTUS_MCP_CAPS";

    private const string DefaultDialogPolicy = "ask";

    /// <summary>What <c>--dialogs</c> accepts. Listed once so the help text and the error agree.</summary>
    private static readonly string[] DialogPolicies = ["accept", "dismiss", "ask"];

    /// <summary>The tool group names, for the help text and the error that lists them.</summary>
    private static string KnownCapabilities => string.Join(", ", ToolCapabilities.All);

    private static string UnknownCapabilityMessage(string name)
        => $"Unknown --caps value '{name}'. Use one or more of: {KnownCapabilities}.";

    /// <summary>
    /// Reads the tool group names out of what was typed. A group may be named on a flag of its
    /// own or in a comma-separated list, because both spellings are natural and a client
    /// configuration that gets it wrong is awkward to debug from inside an agent.
    /// </summary>
    private static string[] SplitCapabilities(IEnumerable<string>? tokens)
        => tokens is null
            ? []
            : tokens
                .SelectMany(token => token.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToArray();

    public static Command Build()
    {
        var headlessOpt = new Option<bool>("--headless")
        {
            Description = "Run the browser without a visible window",
            DefaultValueFactory = _ => true,
        };
        var channelOpt = new Option<string>("--channel")
        {
            Description = "Browser channel to drive (chromium, chrome, edge, firefox)",
            DefaultValueFactory = _ => "chromium",
        };
        var connectOpt = new Option<string?>("--connect")
        {
            Description = "Drive a browser that is already running instead of starting one. Takes the "
                + "debugging endpoint it was started with (http://127.0.0.1:9222) or its CDP WebSocket "
                + "URL. The browser is never closed by the server",
        };
        var httpOpt = new Option<bool>("--http")
        {
            Description = "Serve over Streamable HTTP for concurrent remote clients instead of stdio",
            DefaultValueFactory = _ => false,
        };
        var hostOpt = new Option<string>("--host")
        {
            Description = "Host/interface to bind when --http is set",
            DefaultValueFactory = _ => "127.0.0.1",
        };
        var portOpt = new Option<int>("--port")
        {
            Description = "TCP port to listen on when --http is set",
            DefaultValueFactory = _ => 8931,
        };
        var tokenOpt = new Option<string?>("--token")
        {
            Description =
                "Bearer token required on every HTTP request (or set " + TokenEnvVar + "). "
                + "Required when binding a non-loopback host.",
        };
        var viewportOpt = new Option<string?>("--viewport")
        {
            Description = "Viewport size for every page as WIDTHxHEIGHT, e.g. 1920x1080 (default 1280x800)",
        };
        var recordVideoOpt = new Option<string?>("--record-video")
        {
            Description = "Record a video of every page into this directory (MJPEG AVI, one file per page)",
        };
        var showCursorOpt = new Option<bool>("--show-cursor")
        {
            Description = "Draw an on-screen pseudo-cursor in screenshots and recordings (follows the "
                + "element cursor style and shows a click effect); enables natural mouse motion "
                + "unless --natural-mouse is set explicitly",
            DefaultValueFactory = _ => false,
        };
        var naturalMouseOpt = new Option<bool?>("--natural-mouse")
        {
            Description = "Humanize mouse movement with curved, eased paths. Defaults to the value of "
                + "--show-cursor; pass --natural-mouse false to keep the cursor without it",
        };
        var executablePathOpt = new Option<string?>("--executable-path")
        {
            Description = "Start this browser binary instead of resolving one from --channel. Pins a session "
                + "to an exact build",
        };
        var browserArgOpt = new Option<string[]>("--browser-arg")
        {
            Description = "Extra command-line argument for the browser. Attach the value with '=' and repeat "
                + "the flag for more than one: --browser-arg=--no-sandbox --browser-arg=--disable-dev-shm-usage. "
                + "Chromium refuses to start as root without --no-sandbox, which is the usual case in a container",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var userDataDirOpt = new Option<string?>("--user-data-dir")
        {
            Description = "Browser profile directory. Cookies, history, and signed-in sessions persist in it "
                + "between runs instead of starting clean",
        };
        var storageStateOpt = new Option<string?>("--storage-state")
        {
            Description = "Seed every context with the cookies and local storage saved in this file, as "
                + "written by IBrowserContext.StorageStateAsync",
        };
        var proxyServerOpt = new Option<string?>("--proxy-server")
        {
            Description = "Send browser traffic through this proxy, e.g. http://127.0.0.1:8080",
        };
        var proxyBypassOpt = new Option<string?>("--proxy-bypass")
        {
            Description = "Comma-separated hosts that skip --proxy-server, e.g. localhost,*.internal",
        };
        var userAgentOpt = new Option<string?>("--user-agent")
        {
            Description = "User agent string every page reports",
        };
        var localeOpt = new Option<string?>("--locale")
        {
            Description = "Locale every page reports, e.g. en-GB",
        };
        var timezoneOpt = new Option<string?>("--timezone")
        {
            Description = "Time zone every page reports, e.g. Europe/Berlin",
        };
        var timeoutOpt = new Option<int?>("--timeout")
        {
            Description = "How long an element action waits for its target, in milliseconds",
        };
        var navigationTimeoutOpt = new Option<int?>("--navigation-timeout")
        {
            Description = "How long a navigation waits to finish, in milliseconds",
        };
        var settleOpt = new Option<int?>("--settle")
        {
            Description = "How long an action waits, after the browser accepts it, for the page to show what it "
                + $"did before the result is written, in milliseconds (default {McpServerLaunchOptions.DefaultSettleMilliseconds})",
        };
        settleOpt.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<int?>() is < 0)
                result.AddError("--settle must be zero or more milliseconds.");
        });
        var dialogsOpt = new Option<string>("--dialogs")
        {
            Description = "What becomes of a JavaScript dialog the page raises: accept, dismiss, or ask to "
                + "leave it pending for the agent to answer",
            DefaultValueFactory = _ => DefaultDialogPolicy,
        };
        dialogsOpt.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string?>();
            if (value is not null && !DialogPolicies.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                result.AddError(
                    $"Invalid --dialogs value '{value}'. Use one of: {string.Join(", ", DialogPolicies)}.");
            }
        });
        var configOpt = new Option<string?>("--config")
        {
            Description = "Read defaults from this motus.config.json file. Anything given on the command line "
                + "wins over it",
        };
        var capsOpt = new Option<string[]>("--caps")
        {
            Description = "Add optional tool groups to the catalog: " + KnownCapabilities + ". Separate them "
                + "with commas or repeat the flag (or set " + CapsEnvVar + " to a comma-separated list). "
                + "The tools an agent needs to read and drive a page are always there; these are added "
                + "to them",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        capsOpt.Validators.Add(result =>
        {
            foreach (var name in SplitCapabilities(result.GetValueOrDefault<string[]?>()))
            {
                if (!ToolCapabilities.IsKnown(name))
                    result.AddError(UnknownCapabilityMessage(name));
            }
        });
        var outputDirOpt = new Option<string?>("--output-dir")
        {
            Description = "Directory that tools writing a file (trace_stop, har_stop, video_start) resolve "
                + "their paths inside. Defaults to a directory for this run under the temporary directory, "
                + "printed at startup",
        };
        var allowFileAccessOpt = new Option<bool>("--allow-unrestricted-file-access")
        {
            Description = "Let tools read and write anywhere on the machine and open file:// URLs. Off by "
                + "default: an agent acts partly on instructions that came from the pages it visited",
            DefaultValueFactory = _ => false,
        };
        var allowAttachOpt = new Option<bool>("--allow-attach")
        {
            Description = "Let the browser_attach tool point the session at a browser that is already "
                + "running. Implied by --connect. Off by default",
            DefaultValueFactory = _ => false,
        };

        var cmd = new Command("mcp", "Run the Motus MCP server for agent clients (stdio by default, or --http)")
        {
            headlessOpt,
            channelOpt,
            connectOpt,
            httpOpt,
            hostOpt,
            portOpt,
            tokenOpt,
            viewportOpt,
            recordVideoOpt,
            showCursorOpt,
            naturalMouseOpt,
            executablePathOpt,
            browserArgOpt,
            userDataDirOpt,
            storageStateOpt,
            proxyServerOpt,
            proxyBypassOpt,
            userAgentOpt,
            localeOpt,
            timezoneOpt,
            timeoutOpt,
            navigationTimeoutOpt,
            settleOpt,
            dialogsOpt,
            configOpt,
            capsOpt,
            outputDirOpt,
            allowFileAccessOpt,
            allowAttachOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            // A named file rather than the one that happens to sit above the working directory, so
            // an agent client that is launched from anywhere still gets the settings meant for it.
            // Everything read from it is a default: a value typed on the command line outranks it.
            MotusRootConfig? config = null;
            if (parseResult.GetValue(configOpt) is { Length: > 0 } configPath)
            {
                if (!File.Exists(configPath))
                {
                    await Console.Error.WriteLineAsync($"Config file not found: {configPath}");
                    return 1;
                }

                try
                {
                    config = MotusConfigLoader.LoadFrom(await File.ReadAllTextAsync(configPath, ct));
                }
                catch (Exception ex)
                {
                    await Console.Error.WriteLineAsync($"Could not read '{configPath}': {ex.Message}");
                    return 1;
                }
            }

            bool Typed(Option option) => parseResult.GetResult(option) is { Implicit: false };

            if (!TryResolveCapabilities(
                    Typed(capsOpt) ? parseResult.GetValue(capsOpt) : null,
                    out var caps,
                    out var capsError,
                    config?.Mcp?.Caps))
            {
                await Console.Error.WriteLineAsync(capsError);
                return 1;
            }

            var headless = Typed(headlessOpt)
                ? parseResult.GetValue(headlessOpt)
                : config?.Launch?.Headless ?? parseResult.GetValue(headlessOpt);

            var channelText = parseResult.GetValue(channelOpt)!;
            var channelNamed = Typed(channelOpt);
            if (!channelNamed && config?.Launch?.Channel is { Length: > 0 } configChannel)
            {
                channelText = configChannel;
                channelNamed = true;
            }

            var useHttp = parseResult.GetValue(httpOpt);

            ViewportSize? viewport = null;
            var viewportText = parseResult.GetValue(viewportOpt);
            if (viewportText is not null)
            {
                viewport = ParseViewport(viewportText);
                if (viewport is null)
                {
                    await Console.Error.WriteLineAsync(
                        $"Invalid --viewport value '{viewportText}'. Expected WIDTHxHEIGHT, e.g. 1280x800.");
                    return 1;
                }
            }
            else if (config?.Context?.Viewport is { Width: > 0, Height: > 0 } configViewport)
            {
                viewport = new ViewportSize(configViewport.Width.Value, configViewport.Height.Value);
            }

            var endpoint = parseResult.GetValue(connectOpt);
            var attaching = !string.IsNullOrEmpty(endpoint);

            // Resolving a browser to start is pointless when one is already running and waiting to
            // be connected to.
            BrowserChannel? channel = null;
            string? executablePath = null;
            if (!attaching)
            {
                var requestedPath = parseResult.GetValue(executablePathOpt)
                    ?? (config?.Launch?.ExecutablePath is { Length: > 0 } p ? p : null);

                if (!TryResolveBrowser(channelText, channelNamed, requestedPath, out channel, out executablePath,
                        out var browserError))
                {
                    await Console.Error.WriteLineAsync(browserError);
                    return 1;
                }
            }

            StorageState? storageState = null;
            if (parseResult.GetValue(storageStateOpt) is { Length: > 0 } storageStatePath)
            {
                storageState = LoadStorageState(storageStatePath, out var storageStateError);
                if (storageState is null)
                {
                    await Console.Error.WriteLineAsync(storageStateError);
                    return 1;
                }
            }

            ProxySettings? proxy = null;
            if (parseResult.GetValue(proxyServerOpt) is { Length: > 0 } proxyServer)
            {
                proxy = new ProxySettings(proxyServer, parseResult.GetValue(proxyBypassOpt));
            }
            else if (parseResult.GetValue(proxyBypassOpt) is not null)
            {
                await Console.Error.WriteLineAsync("--proxy-bypass needs --proxy-server; there is nothing to bypass.");
                return 1;
            }

            // Natural motion defaults to the cursor flag so a single --show-cursor gives a
            // legible, human-looking capture; --natural-mouse overrides either way.
            var showCursor = parseResult.GetValue(showCursorOpt);
            var naturalMouse = parseResult.GetValue(naturalMouseOpt) ?? showCursor;

            if (attaching)
            {
                await ReportOptionsThatDoNotApplyAsync(
                    parseResult,
                    [headlessOpt, channelOpt, viewportOpt, recordVideoOpt, showCursorOpt, naturalMouseOpt,
                     executablePathOpt, browserArgOpt, userDataDirOpt, storageStateOpt, proxyServerOpt,
                     proxyBypassOpt, userAgentOpt, localeOpt, timezoneOpt]);

                if (useHttp)
                {
                    await Console.Error.WriteLineAsync(
                        "Warning: --http gives each client its own isolated browser, but --connect points "
                        + "every session at the same one. Clients will share tabs and cookies.");
                }
            }

            var browserArgs = parseResult.GetValue(browserArgOpt);
            var actionTimeout = parseResult.GetValue(timeoutOpt) ?? config?.Locator?.Timeout;
            var locale = parseResult.GetValue(localeOpt)
                ?? (config?.Context?.Locale is { Length: > 0 } configLocale ? configLocale : null);

            // Naming the directory here rather than leaving it to the server means it can be
            // printed before the first tool call, so whoever started the server knows where to
            // look for a trace or a video without asking the agent.
            var outputDirectory = parseResult.GetValue(outputDirOpt) is { Length: > 0 } chosen
                ? Path.GetFullPath(chosen)
                : SecurityPolicy.CreateDefaultOutputDirectory();

            var defaults = new McpServerLaunchOptions();
            var launchOptions = new McpServerLaunchOptions
            {
                Endpoint = attaching ? endpoint : null,
                Headless = headless,
                ExecutablePath = executablePath,
                Channel = channel,
                Viewport = viewport ?? defaults.Viewport,
                RecordVideoDir = parseResult.GetValue(recordVideoOpt),
                ShowCursor = showCursor,
                NaturalMouseMotion = naturalMouse,
                BrowserArgs = browserArgs is { Length: > 0 } ? browserArgs : null,
                UserDataDir = parseResult.GetValue(userDataDirOpt),
                StorageState = storageState,
                Proxy = proxy,
                UserAgent = parseResult.GetValue(userAgentOpt),
                Locale = locale,
                TimezoneId = parseResult.GetValue(timezoneOpt),
                ActionTimeout = actionTimeout,
                NavigationTimeout = parseResult.GetValue(navigationTimeoutOpt),
                SettleTimeout = parseResult.GetValue(settleOpt),
                Dialogs = (parseResult.GetValue(dialogsOpt) ?? DefaultDialogPolicy).ToLowerInvariant(),
                OutputDirectory = outputDirectory,
                AllowUnrestrictedFileAccess = parseResult.GetValue(allowFileAccessOpt),
                AllowAttach = parseResult.GetValue(allowAttachOpt),
                Capabilities = caps.Length > 0 ? caps : null,
            };

            await Console.Error.WriteLineAsync(
                $"Output directory: {outputDirectory}. Tools that write a file put it here; pass "
                + "--output-dir to change it.");

            if (!useHttp)
            {
                await McpServerHost.RunAsync(launchOptions, ct);
                return 0;
            }

            var token = parseResult.GetValue(tokenOpt)
                ?? Environment.GetEnvironmentVariable(TokenEnvVar);

            var httpOptions = new McpHttpServerOptions
            {
                Host = parseResult.GetValue(hostOpt)!,
                Port = parseResult.GetValue(portOpt),
                Token = string.IsNullOrEmpty(token) ? null : token,
                LaunchOptions = launchOptions,
            };

            try
            {
                await McpHttpServerHost.StartAsync(httpOptions, ct);
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                // The host refuses to bind a non-loopback address without a token; surface the
                // reason instead of a stack trace.
                await Console.Error.WriteLineAsync(ex.Message);
                return 1;
            }
        });

        return cmd;
    }

    /// <summary>
    /// Says which of the given options were passed but have nothing to act on, because the browser
    /// was started by somebody else and its context is adopted rather than created.
    /// </summary>
    /// <remarks>
    /// Only options the user actually typed are named. Silently ignoring them would leave someone
    /// wondering for a while why <c>--viewport</c> changed nothing.
    /// </remarks>
    private static async Task ReportOptionsThatDoNotApplyAsync(
        System.CommandLine.ParseResult parseResult, IReadOnlyList<Option> candidates)
    {
        var given = candidates
            .Where(option => parseResult.GetResult(option) is { Implicit: false })
            .Select(option => option.Name)
            .ToArray();

        if (given.Length == 0)
            return;

        await Console.Error.WriteLineAsync(
            $"Note: {string.Join(", ", given)} {(given.Length == 1 ? "has" : "have")} no effect with "
            + "--connect. The browser is already running, and its context is adopted rather than created. "
            + "Use the resize tool to change the viewport of a page.");
    }

    /// <summary>
    /// Works out which browser to start: an explicit binary, one <c>motus install</c> downloaded for
    /// the channel, or one the machine already has. Returns false, with the message to print, when
    /// no answer is the browser that was actually asked for.
    /// </summary>
    /// <remarks>
    /// A channel someone named is binding. Without that rule a missing Firefox quietly became
    /// whichever Chromium the machine had, and the session went on reporting itself as Firefox. A
    /// channel nobody named leaves the choice open, which is what lets the framework auto-detect an
    /// installed browser as it always has, so the default path is unchanged.
    /// </remarks>
    internal static bool TryResolveBrowser(
        string channelText,
        bool channelNamed,
        string? executablePath,
        out BrowserChannel? channel,
        out string? resolvedPath,
        out string? error)
    {
        channel = null;
        resolvedPath = null;
        error = null;

        if (!Enum.TryParse<BrowserChannel>(channelText, ignoreCase: true, out var parsed))
        {
            error = $"Unknown --channel value '{channelText}'. Use one of: chromium, chrome, edge, firefox.";
            return false;
        }

        if (!string.IsNullOrEmpty(executablePath))
        {
            if (!File.Exists(executablePath))
            {
                error = $"Browser executable not found: {executablePath}";
                return false;
            }

            channel = channelNamed ? parsed : null;
            resolvedPath = executablePath;
            return true;
        }

        // The generic marker records a path without recording which browser it points at, so it
        // answers only when nobody named a channel.
        resolvedPath = channelNamed
            ? BrowserPathHelper.ResolveChannel(channelText)
            : BrowserPathHelper.Resolve(channelText);

        if (!channelNamed)
            return true;

        if (resolvedPath is null && !BrowserFinder.CandidatesForChannel(parsed).Any(File.Exists))
        {
            var name = channelText.ToLowerInvariant();
            error = $"No {name} installation found. Run 'motus install --channel {name}' "
                + "or pass --executable-path.";
            return false;
        }

        channel = parsed;
        return true;
    }

    /// <summary>
    /// Works out which optional tool groups to register, from what was typed or, failing that, from
    /// <c>MOTUS_MCP_CAPS</c> and then the config file. Returns false, with the message to print,
    /// when a name is not one of the groups.
    /// </summary>
    /// <param name="requested">The groups named on the command line, or null when none were.</param>
    /// <param name="capabilities">The group names to register.</param>
    /// <param name="error">Why the answer was refused, or null.</param>
    /// <param name="fromConfig">The groups the config file named, or null when it named none.</param>
    /// <param name="envReader">
    /// Where to read the environment variable from. Tests pass their own so no real variable has to
    /// be set for the process.
    /// </param>
    /// <remarks>
    /// The command line is a whole answer when it has one: a client that passes <c>--caps</c> says
    /// exactly which groups it wants rather than adding to whatever the environment or the file
    /// happened to name, the same way every other option here outranks them. The variable sits
    /// between the two, matching the order the rest of the configuration documents, and is read
    /// here rather than with the config file because that file is loaded only when
    /// <c>--config</c> names it.
    /// </remarks>
    internal static bool TryResolveCapabilities(
        IEnumerable<string>? requested,
        out string[] capabilities,
        out string? error,
        IEnumerable<string>? fromConfig = null,
        Func<string, string?>? envReader = null)
    {
        var selection = requested;
        if (selection is null
            && (envReader ?? Environment.GetEnvironmentVariable)(CapsEnvVar) is { Length: > 0 } fromEnvironment)
            selection = [fromEnvironment];

        capabilities = SplitCapabilities(selection ?? fromConfig);

        foreach (var name in capabilities)
        {
            if (!ToolCapabilities.IsKnown(name))
            {
                error = UnknownCapabilityMessage(name);
                capabilities = [];
                return false;
            }
        }

        error = null;
        return true;
    }

    /// <summary>
    /// Reads cookies and local storage from a file written by
    /// <c>IBrowserContext.StorageStateAsync(path)</c>. Returns null, with the message to print, when
    /// the file cannot be read as one.
    /// </summary>
    internal static StorageState? LoadStorageState(string path, out string? error)
    {
        error = null;

        if (!File.Exists(path))
        {
            error = $"Storage state file not found: {path}";
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<StorageState>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (state is null)
            {
                error = $"Storage state file '{path}' is empty.";
                return null;
            }

            return state;
        }
        catch (JsonException ex)
        {
            error = $"Could not read storage state from '{path}': {ex.Message}";
            return null;
        }
    }

    private static ViewportSize? ParseViewport(string text)
    {
        var parts = text.Split(['x', 'X'], 2);
        if (parts.Length == 2
            && int.TryParse(parts[0], out var width) && width > 0
            && int.TryParse(parts[1], out var height) && height > 0)
        {
            return new ViewportSize(width, height);
        }

        return null;
    }
}
