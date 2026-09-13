using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Motus.Tests.Browser;

/// <summary>
/// An endpoint that behaves like a browser up to a chosen point and then goes quiet, so a
/// connection can be made to run out of time in one named place rather than somewhere unknown.
/// </summary>
/// <remarks>
/// Nothing here is a browser. The HTTP side answers <c>/json/version</c> the way a browser does,
/// and the socket side either leaves the upgrade request unanswered or completes the handshake and
/// then says nothing at all. Those are the two ways a real connection stalls after discovery.
/// </remarks>
internal sealed class StallingCdpEndpoint : IDisposable
{
    /// <summary>The constant the WebSocket handshake mixes into the client's key.</summary>
    private const string HandshakeGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    private readonly HttpListener _http = new();
    private readonly TcpListener _socket;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<TcpClient> _held = [];
    private readonly bool _completeHandshake;
    private readonly string _webSocketUrl;

    /// <summary>The HTTP debugging endpoint to point a connection at.</summary>
    internal string HttpEndpoint { get; }

    /// <param name="completeHandshake">
    /// When true the WebSocket opens and then answers no command. When false the upgrade request
    /// is accepted at the TCP level and never answered.
    /// </param>
    internal StallingCdpEndpoint(bool completeHandshake)
    {
        _completeHandshake = completeHandshake;

        _socket = new TcpListener(IPAddress.Loopback, 0);
        _socket.Start();
        _webSocketUrl = $"ws://127.0.0.1:{((IPEndPoint)_socket.LocalEndpoint).Port}/devtools/browser/stalled";

        HttpEndpoint = $"http://127.0.0.1:{AllocateFreePort()}";
        _http.Prefixes.Add(HttpEndpoint + "/");
        _http.Start();

        _ = Task.Run(ServeHttpAsync);
        _ = Task.Run(AcceptSocketsAsync);
    }

    private async Task ServeHttpAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _http.GetContextAsync().ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            var body = Encoding.UTF8.GetBytes($$"""{"webSocketDebuggerUrl": "{{_webSocketUrl}}"}""");
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = body.Length;
            await context.Response.OutputStream.WriteAsync(body).ConfigureAwait(false);
            context.Response.Close();
        }
    }

    private async Task AcceptSocketsAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _socket.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            // Held so the connection stays open for as long as the test needs it. Letting it fall
            // out of scope would close it, and a closed connection is a refusal rather than a
            // stall.
            lock (_held)
                _held.Add(client);

            if (_completeHandshake)
                await CompleteHandshakeAsync(client).ConfigureAwait(false);
        }
    }

    private static async Task CompleteHandshakeAsync(TcpClient client)
    {
        try
        {
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            var request = Encoding.ASCII.GetString(buffer, 0, read);

            var key = request
                .Split("\r\n")
                .FirstOrDefault(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                ?.Split(':', 2)[1]
                .Trim();

            if (key is null)
                return;

            var accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + HandshakeGuid)));

            var response = "HTTP/1.1 101 Switching Protocols\r\n"
                           + "Upgrade: websocket\r\n"
                           + "Connection: Upgrade\r\n"
                           + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";

            await stream.WriteAsync(Encoding.ASCII.GetBytes(response)).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        catch
        {
            // The test is finishing and took the connection with it.
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

        lock (_held)
        {
            foreach (var client in _held)
            {
                try { client.Close(); } catch { /* already gone */ }
            }

            _held.Clear();
        }

        try { _socket.Stop(); } catch { /* already stopped */ }
        try { _http.Close(); } catch { /* already closed */ }

        _cts.Dispose();
    }
}
