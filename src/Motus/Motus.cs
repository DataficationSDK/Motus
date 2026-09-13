using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Public entry point for launching and connecting to browsers.
/// </summary>
public static class MotusLauncher
{
    /// <summary>
    /// Launches a new browser process and connects to it via CDP (Chromium) or WebDriver BiDi (Firefox).
    /// </summary>
    public static async Task<IBrowser> LaunchAsync(LaunchOptions? options = null, CancellationToken ct = default)
    {
        options = ConfigMerge.ApplyConfig(options ?? new LaunchOptions());

        var executablePath = BrowserFinder.Resolve(options.Channel, options.ExecutablePath);
        var isFirefox = IsFirefoxChannel(options.Channel, executablePath);

        // A browser Motus starts is driven over a pipe wherever that can be arranged, because a
        // pipe is the only thing that tells the browser its launcher has gone. Firefox has no pipe
        // mode, and Windows offers no way to hand a child the descriptors Chromium expects, so
        // both keep the debugging port. What replaces the pipe on Windows is a job object.
        var usePipe = !isFirefox && !OperatingSystem.IsWindows();
        var port = usePipe ? 0 : AllocateFreePort();

        string profileOrDataDir;
        bool ownsTempDir;

        ProcessStartInfo psi;

        if (isFirefox)
        {
            var (profileDir, ownsTemp) = FirefoxProfileManager.CreateTempProfile(options.UserDataDir);
            profileOrDataDir = profileDir;
            ownsTempDir = ownsTemp;

            var (args, envVars) = FirefoxArgs.Build(options, port, profileDir);

            psi = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var (key, value) in envVars)
                psi.Environment[key] = value;

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
        }
        else
        {
            ownsTempDir = options.UserDataDir is null;
            profileOrDataDir = options.UserDataDir ?? CreateTempUserDataDir();

            var args = ChromiumArgs.Build(options, profileOrDataDir, usePipe ? null : port);

            psi = new ProcessStartInfo
            {
                FileName = usePipe ? "/bin/sh" : executablePath,
                UseShellExecute = false,
                RedirectStandardInput = usePipe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            if (usePipe)
            {
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add(PipeLaunchScript);
                psi.ArgumentList.Add(executablePath);
            }

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
        }

        var process = Process.Start(psi)
                      ?? throw new InvalidOperationException($"Failed to start browser process: {executablePath}");

        // A redirected stream nobody reads is a pipe that fills and then blocks the browser writing
        // to it. Draining begins before the first command is sent. Firefox's stderr is left alone:
        // the endpoint reader below reads it line by line and keeps doing so for the life of the
        // process. A browser on a pipe has its standard output carrying the protocol itself, and
        // only its diagnostics are drained.
        var output = isFirefox
            ? BrowserOutputDrain.Start(process.StandardOutput)
            : usePipe
                ? BrowserOutputDrain.Start(process.StandardError)
                : BrowserOutputDrain.Start(process.StandardOutput, process.StandardError);

        // On Windows the browser keeps a debugging port, so nothing about the connection tells it
        // this process has gone. The job object does that instead.
        var guard = !isFirefox && OperatingSystem.IsWindows()
            ? WindowsProcessGuard.TryAdopt(process)
            : null;

        try
        {
            var timeout = TimeSpan.FromMilliseconds(options.Timeout);

            Uri? wsEndpoint = null;
            if (!usePipe)
            {
                try
                {
                    wsEndpoint = isFirefox
                        ? await FirefoxEndpointReader
                            .WaitForEndpointAsync(new ProcessStderrAdapter(process), timeout, ct).ConfigureAwait(false)
                        : await new CdpEndpointPoller()
                            .WaitForEndpointAsync(port, timeout, ct).ConfigureAwait(false);
                }
                catch (MotusTimeoutException ex)
                {
                    // A browser that never offered an endpoint has usually said why on its way down.
                    throw new MotusTimeoutException(timeoutDuration: timeout, message: $"{ex.Message}{output.Describe()}");
                }
            }

            // Everything from here to the first answered command is bounded by the launch timeout.
            // Neither the WebSocket handshake nor the opening command carries a bound of its own, so
            // a browser that accepts a connection and then answers nothing on it would otherwise
            // hold the caller for good.
            using var readyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readyCts.CancelAfter(timeout);

            try
            {
                IMotusTransport transport;
                IMotusSessionRegistry registry;
                var slowMo = TimeSpan.FromMilliseconds(options.SlowMo);

                if (usePipe)
                {
                    // The browser is already on the other end of these, so the transport is started
                    // rather than connected.
                    var pipeSocket = new CdpPipeSocket(
                        process.StandardInput.BaseStream, process.StandardOutput.BaseStream);

                    var pipeTransport = new CdpTransport(pipeSocket, slowMo);
                    pipeTransport.Start();

                    transport = pipeTransport;
                    registry = new CdpSessionRegistry(pipeTransport);
                }
                else if (isFirefox)
                {
                    // TODO: BiDiTransport does not support SlowMo yet
                    var socket = new CdpSocket();
                    var bidiTransport = new BiDiTransport(socket);
                    await bidiTransport.ConnectAsync(wsEndpoint!, readyCts.Token).ConfigureAwait(false);

                    var sessionId = await bidiTransport.CreateSessionAsync(readyCts.Token).ConfigureAwait(false);

                    transport = bidiTransport;
                    registry = new BiDiSessionRegistry(bidiTransport, sessionId);
                }
                else
                {
                    var socket = new CdpSocket();
                    var cdpTransport = new CdpTransport(socket, slowMo);
                    await cdpTransport.ConnectAsync(wsEndpoint!, readyCts.Token).ConfigureAwait(false);

                    transport = cdpTransport;
                    registry = new CdpSessionRegistry(cdpTransport);
                }

                var browser = new Browser(
                    transport, registry, process,
                    ownsTempDir ? profileOrDataDir : null,
                    options.HandleSIGINT, options.HandleSIGTERM,
                    options, output, processGuard: guard);

                await browser.InitializeAsync(readyCts.Token).ConfigureAwait(false);
                return browser;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new MotusTimeoutException(
                    timeoutDuration: timeout,
                    message: usePipe
                        ? $"Browser started but did not answer on its pipe within "
                          + $"{timeout.TotalSeconds}s.{output.Describe()}"
                        : $"Browser offered a debugging endpoint but did not finish connecting "
                          + $"within {timeout.TotalSeconds}s.{output.Describe()}");
            }
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            process.Dispose();
            guard?.Dispose();

            if (ownsTempDir)
            {
                try { Directory.Delete(profileOrDataDir, recursive: true); } catch { }
            }

            throw;
        }
    }

    /// <summary>
    /// Hands the browser the two file descriptors it reads and writes CDP on.
    /// </summary>
    /// <remarks>
    /// Chromium started with <c>--remote-debugging-pipe</c> reads commands on descriptor 3 and
    /// writes on descriptor 4, and the process API offers no way to give a child an arbitrary
    /// descriptor. A shell does. It copies this process's end of the redirected standard input
    /// onto 3 and of the redirected standard output onto 4, points standard output at nothing so
    /// that the browser's own logging cannot be mistaken for protocol traffic, and then replaces
    /// itself with the browser, so the process handle still refers to the browser and nothing sits
    /// between the two.
    /// </remarks>
    private const string PipeLaunchScript = "exec \"$0\" \"$@\" 3<&0 4>&1 1>/dev/null";

    /// <summary>
    /// Connects to an existing browser instance via its CDP WebSocket endpoint.
    /// </summary>
    public static Task<IBrowser> ConnectAsync(string wsEndpoint, CancellationToken ct = default)
        => ConnectAsync(wsEndpoint, options: null, ct);

    /// <summary>
    /// Connects to a browser that is already running. The endpoint may be a CDP WebSocket URL, or
    /// the browser's HTTP debugging endpoint, from which the WebSocket URL is resolved.
    /// </summary>
    /// <remarks>
    /// The returned browser is not owned: neither closing nor disposing it ends the browser
    /// process. By default the contexts and pages already open are adopted, so they can be driven
    /// straight away.
    /// </remarks>
    public static async Task<IBrowser> ConnectAsync(
        string endpoint, ConnectOptions? options, CancellationToken ct = default)
    {
        options ??= new ConnectOptions();

        var timeout = TimeSpan.FromMilliseconds(options.Timeout);

        // Connecting is bounded end to end. Neither the WebSocket handshake nor target adoption
        // carries a bound of its own, so an endpoint that accepts a connection and then answers
        // nothing on it would otherwise hold the caller for good.
        using var readyCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        readyCts.CancelAfter(timeout);

        // Each step is bounded separately so that running out of time says which one was still
        // waiting. They share one token, so the budget for all of them together is still the
        // caller's timeout and no step can extend it.
        var poller = new CdpEndpointPoller();

        // The poller keeps a deadline of its own and the budget above cancels it at the same
        // moment, so which of the two speaks first is a matter of timing. Both mean the same thing
        // and are reported in the same words.
        Uri wsEndpoint;
        try
        {
            wsEndpoint = await ResolveWebSocketEndpointAsync(endpoint, timeout, poller, readyCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is MotusTimeoutException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            throw new MotusTimeoutException(
                timeoutDuration: timeout,
                message: $"Did not finish connecting to {endpoint} within {timeout.TotalSeconds}s. "
                         + $"Its debugging endpoint never answered with a WebSocket URL.{poller.Describe()}");
        }

        var slowMo = TimeSpan.FromMilliseconds(options.SlowMo);
        var socket = new CdpSocket();
        var transport = new CdpTransport(socket, slowMo);

        try
        {
            await transport.ConnectAsync(wsEndpoint, readyCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await transport.DisposeAsync().ConfigureAwait(false);
            throw new MotusTimeoutException(
                timeoutDuration: timeout,
                message: $"Did not finish connecting to {endpoint} within {timeout.TotalSeconds}s. "
                         + $"It named {wsEndpoint}, and that WebSocket never finished opening.");
        }

        var registry = new CdpSessionRegistry(transport);
        var browser = new Browser(
            transport, registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false,
            adoptExistingTargets: options.AdoptExistingTargets);

        try
        {
            await browser.InitializeAsync(readyCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await browser.DisposeAsync().ConfigureAwait(false);
            throw new MotusTimeoutException(
                timeoutDuration: timeout,
                message: $"Did not finish connecting to {endpoint} within {timeout.TotalSeconds}s. "
                         + "The WebSocket opened, and the browser did not finish reporting its "
                         + "version and taking over the tabs already open.");
        }

        return browser;
    }

    /// <summary>
    /// Accepts either form of endpoint a caller is likely to have: the WebSocket URL itself, or
    /// the HTTP debugging endpoint the browser was started with, which is the one a caller who
    /// chose the port already knows.
    /// </summary>
    private static async Task<Uri> ResolveWebSocketEndpointAsync(
        string endpoint, TimeSpan timeout, CdpEndpointPoller poller, CancellationToken ct)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            throw new ArgumentException(
                $"'{endpoint}' is not a valid browser endpoint. Expected a CDP WebSocket URL "
                + "or an HTTP debugging endpoint such as http://127.0.0.1:9222.",
                nameof(endpoint));

        if (uri.Scheme is "ws" or "wss")
            return uri;

        if (uri.Scheme is "http" or "https")
            return await poller.WaitForEndpointAsync(uri, timeout, ct).ConfigureAwait(false);

        throw new ArgumentException(
            $"Browser endpoint scheme '{uri.Scheme}' is not supported. Expected ws, wss, http, or https.",
            nameof(endpoint));
    }

    internal static bool IsFirefoxChannel(BrowserChannel? channel, string? executablePath)
    {
        if (channel == BrowserChannel.Firefox)
            return true;

        if (executablePath is not null)
        {
            var fileName = Path.GetFileName(executablePath);
            return fileName.Contains("firefox", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static int AllocateFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string CreateTempUserDataDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "motus-profile-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }
}
