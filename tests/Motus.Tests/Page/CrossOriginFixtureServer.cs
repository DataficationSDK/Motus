using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Motus.Tests.Page;

/// <summary>
/// Serves a small set of documents from two origins, so a page can embed a frame the browser has
/// to put in a process of its own.
/// </summary>
/// <remarks>
/// Every other integration test navigates to a <c>data:</c> URL, which cannot be cross-origin to
/// anything and therefore cannot produce an out-of-process frame at all. Two loopback hosts are
/// used rather than two ports, because a port alone does not make a different site.
/// </remarks>
internal sealed class CrossOriginFixtureServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();

    /// <summary>The origin the outer document is served from.</summary>
    internal string PrimaryOrigin { get; }

    /// <summary>A second origin, which the browser treats as a different site from the first.</summary>
    internal string SecondaryOrigin { get; }

    internal string OuterUrl => PrimaryOrigin + "/outer.html";

    /// <summary>
    /// How far the cross-origin frame is pushed from the top left of the outer document. Large
    /// enough on both axes that a reading taken inside the frame is obviously wrong if the frame's
    /// own offset is not accounted for.
    /// </summary>
    private const int FrameOffsetTop = 120;

    private const int FrameOffsetLeft = 40;

    internal CrossOriginFixtureServer()
    {
        PrimaryOrigin = $"http://127.0.0.1:{AllocateFreePort()}";
        SecondaryOrigin = $"http://localhost:{AllocateFreePort()}";

        _listener.Prefixes.Add(PrimaryOrigin + "/");
        _listener.Prefixes.Add(SecondaryOrigin + "/");

        BuildDocuments();

        _listener.Start();
        _ = Task.Run(ServeAsync);
    }

    private void BuildDocuments()
    {
        // The outer document. The frame is pushed away from the origin on both axes so that a
        // reading taken inside it is obviously wrong if the offset is not accounted for.
        _documents["/outer.html"] = $$"""
            <!doctype html>
            <html><head><style>
              body { margin: 0; }
              #spacer { height: {{FrameOffsetTop}}px; }
              iframe { display: block; margin-left: {{FrameOffsetLeft}}px; width: 400px; height: 300px; border: 0; }
            </style></head>
            <body>
              <script>window.pageGlobal = 'outer';</script>
              <button id="target">main</button>
              <div id="spacer"></div>
              <iframe id="remote" src="{{SecondaryOrigin}}/middle.html"></iframe>
              <iframe id="sandboxed" sandbox="allow-scripts" srcdoc="
                <body>
                  <button id='target'>sandboxed</button>
                  <script>window.marker='sandboxed';</script>
                </body>"></iframe>
            </body></html>
            """;

        // The cross-origin frame, which embeds another cross-origin frame of its own. That second
        // level is what a single round of auto-attach would miss.
        _documents["/middle.html"] = $$"""
            <!doctype html>
            <html><head><style>
              body { margin: 0; }
              #pad { height: 60px; }
              button { display: block; margin: 0; height: 30px; }
              iframe { display: block; width: 200px; height: 100px; border: 0; }
            </style></head>
            <body>
              <script>
                window.marker = 'middle';
                window.clicks = 0;
              </script>
              <div id="pad"></div>
              <button id="target" data-testid="go" role="button" aria-label="Go">middle</button>
              <script>document.getElementById('target').onclick = () => window.clicks++;</script>
              <iframe id="deep" src="{{PrimaryOrigin}}/deep.html"></iframe>
            </body></html>
            """;

        // Two process boundaries from the page, so a click here only lands if the frame offsets
        // accumulate across both.
        _documents["/deep.html"] = """
            <!doctype html>
            <html><head><style>body { margin: 0; } #pad { height: 30px; }</style></head>
            <body>
              <script>window.marker = 'deep'; window.clicks = 0;</script>
              <div id="pad"></div>
              <button id="target">deep</button>
              <script>document.getElementById('target').onclick = () => window.clicks++;</script>
            </body></html>
            """;
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
            var found = _documents.TryGetValue(path, out var body);

            context.Response.StatusCode = found ? 200 : 404;
            context.Response.ContentType = "text/html; charset=utf-8";

            var bytes = Encoding.UTF8.GetBytes(found ? body! : "<!doctype html><body>not found</body>");
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

    /// <summary>
    /// Browser arguments that make each origin land in its own process, so the test is measuring
    /// out-of-process behavior rather than whatever Chromium decides about loopback today.
    /// </summary>
    internal IReadOnlyList<string> IsolationArgs =>
    [
        "--site-per-process",
        $"--isolate-origins={PrimaryOrigin},{SecondaryOrigin}"
    ];

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
