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
        var port = AllocateFreePort();

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

            var args = ChromiumArgs.Build(options, port, profileOrDataDir);

            psi = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
        }

        var process = Process.Start(psi)
                      ?? throw new InvalidOperationException($"Failed to start browser process: {executablePath}");

        try
        {
            var timeout = TimeSpan.FromMilliseconds(options.Timeout);

            IMotusTransport transport;
            IMotusSessionRegistry registry;

            if (isFirefox)
            {
                var stderrSource = new ProcessStderrAdapter(process);
                var wsEndpoint = await FirefoxEndpointReader.WaitForEndpointAsync(stderrSource, timeout, ct)
                    .ConfigureAwait(false);

                // TODO: BiDiTransport does not support SlowMo yet
                var socket = new CdpSocket();
                var bidiTransport = new BiDiTransport(socket);
                await bidiTransport.ConnectAsync(wsEndpoint, ct).ConfigureAwait(false);

                var sessionId = await bidiTransport.CreateSessionAsync(ct).ConfigureAwait(false);

                transport = bidiTransport;
                registry = new BiDiSessionRegistry(bidiTransport, sessionId);
            }
            else
            {
                var wsEndpoint = await CdpEndpointPoller.WaitForEndpointAsync(port, timeout, ct)
                    .ConfigureAwait(false);

                var slowMo = TimeSpan.FromMilliseconds(options.SlowMo);
                var socket = new CdpSocket();
                var cdpTransport = new CdpTransport(socket, slowMo);
                await cdpTransport.ConnectAsync(wsEndpoint, ct).ConfigureAwait(false);

                transport = cdpTransport;
                registry = new CdpSessionRegistry(cdpTransport);
            }

            var browser = new Browser(
                transport, registry, process,
                ownsTempDir ? profileOrDataDir : null,
                options.HandleSIGINT, options.HandleSIGTERM,
                options);

            await browser.InitializeAsync(ct).ConfigureAwait(false);
            return browser;
        }
        catch
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            process.Dispose();

            if (ownsTempDir)
            {
                try { Directory.Delete(profileOrDataDir, recursive: true); } catch { }
            }

            throw;
        }
    }

    /// <summary>
    /// Connects to an existing browser instance via its CDP WebSocket endpoint.
    /// </summary>
    public static async Task<IBrowser> ConnectAsync(string wsEndpoint, CancellationToken ct = default)
    {
        var socket = new CdpSocket();
        var transport = new CdpTransport(socket);
        await transport.ConnectAsync(new Uri(wsEndpoint), ct).ConfigureAwait(false);

        var registry = new CdpSessionRegistry(transport);
        var browser = new Browser(
            transport, registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        await browser.InitializeAsync(ct).ConfigureAwait(false);
        return browser;
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
