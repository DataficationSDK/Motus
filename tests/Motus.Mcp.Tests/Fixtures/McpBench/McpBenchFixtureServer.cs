using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Motus.Mcp.Tests.Fixtures;

/// <summary>
/// Serves the MCP bench page from a loopback origin, so the tests that measure what an agent sees
/// and does run against a page with the shapes that trip agents up: a form under a stack of
/// wrapper elements, a button that opens an alert, a button that logs an error and throws, a
/// link that opens a new tab, content that appears after a delay, a canvas, and a same-origin
/// frame.
/// </summary>
/// <remarks>
/// The documents live beside this class as <c>index.html</c>, <c>frame.html</c>, and
/// <c>other.html</c> and are copied to the test output directory, so the same files can be served
/// by hand for a manual comparison. A real <c>http://</c> origin is needed rather than a
/// <c>data:</c> URL because the new-tab link and the frame both need a URL to resolve against.
/// </remarks>
internal sealed class McpBenchFixtureServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly string _root;
    private readonly List<string> _requests = [];

    /// <summary>The origin the page is served from, without a trailing slash.</summary>
    internal string Origin { get; }

    /// <summary>The bench page itself.</summary>
    internal string IndexUrl => Origin + "/index.html";

    /// <summary>The page the two navigation links point at.</summary>
    internal string OtherUrl => Origin + "/other.html";

    /// <summary>
    /// Every path requested so far, in order. Lets a test prove the browser fetched a document
    /// even when the session cannot see the page that asked for it.
    /// </summary>
    internal IReadOnlyList<string> Requests
    {
        get { lock (_requests) return _requests.ToArray(); }
    }

    internal McpBenchFixtureServer()
    {
        _root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "McpBench");
        if (!File.Exists(Path.Combine(_root, "index.html")))
            throw new FileNotFoundException("The bench fixture was not copied to the test output.", _root);

        Origin = $"http://127.0.0.1:{AllocateFreePort()}";
        _listener.Prefixes.Add(Origin + "/");
        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    private async Task ServeAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path == "/")
                path = "/index.html";

            lock (_requests) _requests.Add(path);

            var file = Path.Combine(_root, path.TrimStart('/'));
            var found = Path.GetExtension(file) == ".html" && File.Exists(file);

            byte[] bytes;
            if (found)
            {
                bytes = await File.ReadAllBytesAsync(file).ConfigureAwait(false);
                context.Response.StatusCode = 200;
            }
            else
            {
                bytes = Encoding.UTF8.GetBytes("<!doctype html><body>not found</body>");
                context.Response.StatusCode = 404;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;

            try
            {
                await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                context.Response.Close();
            }
            catch (Exception)
            {
                // The browser gave up on the request.
            }
        }
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
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
        _listener.Close();
        _cts.Dispose();
    }
}
