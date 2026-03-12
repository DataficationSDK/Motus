using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Public entry point for launching and connecting to Chromium browsers.
/// </summary>
public static class MotusLauncher
{
    /// <summary>
    /// Launches a new browser process and connects to it via CDP.
    /// </summary>
    public static async Task<IBrowser> LaunchAsync(LaunchOptions? options = null, CancellationToken ct = default)
    {
        options = ConfigMerge.ApplyConfig(options ?? new LaunchOptions());

        var executablePath = BrowserFinder.Resolve(options.Channel, options.ExecutablePath);
        var port = AllocateFreePort();

        var ownsTempDir = options.UserDataDir is null;
        var userDataDir = options.UserDataDir ?? CreateTempUserDataDir();

        var args = ChromiumArgs.Build(options, port, userDataDir);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi)
                      ?? throw new InvalidOperationException($"Failed to start browser process: {executablePath}");

        try
        {
            var timeout = TimeSpan.FromMilliseconds(options.Timeout);
            var wsEndpoint = await CdpEndpointPoller.WaitForEndpointAsync(port, timeout, ct);

            var slowMo = TimeSpan.FromMilliseconds(options.SlowMo);
            var socket = new CdpSocket();
            var transport = new CdpTransport(socket, slowMo);
            await transport.ConnectAsync(wsEndpoint, ct);

            var registry = new CdpSessionRegistry(transport);
            var browser = new Browser(
                transport, registry, process,
                ownsTempDir ? userDataDir : null,
                options.HandleSIGINT, options.HandleSIGTERM,
                options);

            await browser.InitializeAsync(ct);
            return browser;
        }
        catch
        {
            // Clean up on failure
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
            }
            process.Dispose();

            if (ownsTempDir)
            {
                try { Directory.Delete(userDataDir, recursive: true); } catch { }
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
        await transport.ConnectAsync(new Uri(wsEndpoint), ct);

        var registry = new CdpSessionRegistry(transport);
        var browser = new Browser(
            transport, registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        await browser.InitializeAsync(ct);
        return browser;
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
