using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Motus.Abstractions;

namespace Motus.Mcp.Tests;

/// <summary>
/// Starts a browser with a debugging port and keeps it running, so the server can be pointed at a
/// browser it did not start.
/// </summary>
/// <remarks>
/// The process is started here rather than through Motus, because ownership is the point: a browser
/// handed over by the launcher would be owned, and the guarantees these tests exist to pin would
/// not be real.
/// </remarks>
internal sealed class RunningBrowserFixture : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);

    private readonly Process _process;
    private readonly string _userDataDir;

    private RunningBrowserFixture(Process process, string userDataDir, int port)
    {
        _process = process;
        _userDataDir = userDataDir;
        Port = port;
    }

    internal int Port { get; }

    internal string HttpEndpoint => $"http://127.0.0.1:{Port}";

    /// <summary>Whether the browser process is still running.</summary>
    internal bool IsRunning => !_process.HasExited;

    /// <summary>
    /// Starts the browser, or returns null when no browser is installed, which is this suite's
    /// standard skip gate.
    /// </summary>
    internal static async Task<RunningBrowserFixture?> TryStartAsync()
    {
        string executablePath;
        try
        {
            executablePath = BrowserFinder.Resolve(channel: null, executablePath: null);
        }
        catch (FileNotFoundException)
        {
            return null;
        }

        var port = AllocateFreePort();
        var userDataDir = Path.Combine(
            Path.GetTempPath(), "motus-mcp-attach-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(userDataDir);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in ChromiumArgs.Build(new LaunchOptions { Headless = true }, port, userDataDir))
            psi.ArgumentList.Add(arg);

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("The browser process did not start.");

        // Redirected streams nobody reads fill and then block the browser writing to them.
        BrowserOutputDrain.Start(process.StandardOutput, process.StandardError);

        await CdpEndpointPoller.WaitForEndpointAsync(port, StartupTimeout, CancellationToken.None)
            .ConfigureAwait(false);

        return new RunningBrowserFixture(process, userDataDir, port);
    }

    private static int AllocateFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public void Dispose()
    {
        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }

        _process.Dispose();

        try { Directory.Delete(_userDataDir, recursive: true); } catch { /* best-effort */ }
    }
}
