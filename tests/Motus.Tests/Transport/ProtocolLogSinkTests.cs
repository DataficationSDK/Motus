using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using Motus.Abstractions;

namespace Motus.Tests.Transport;

[TestClass]
public class ProtocolLogSinkTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "motus-protocol-log-" + Guid.NewGuid().ToString("N")[..8]);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [TestMethod]
    public async Task TwoTransportsSamePath_ShareOneSink_DistinctConnectionTags()
    {
        var path = Path.Combine(_dir, "p.log");
        var options = new ProtocolLogOptions { Path = path };

        var first = ProtocolLog.Open(options, "cdp-ws", "a:1")!;
        var second = ProtocolLog.Open(options, "cdp-ws", "b:2")!;
        Assert.AreNotEqual(first.Tag, second.Tag);

        await first.DisposeAsync();
        Assert.IsTrue(ProtocolLogSink.IsOpen(path), "the file stays open while a connection still logs to it");

        await second.DisposeAsync();
        Assert.IsFalse(ProtocolLogSink.IsOpen(path), "the last connection to let go closes the file");

        var lines = File.ReadAllLines(path);
        Assert.AreEqual(1, lines.Count(l => l.StartsWith("# motus protocol log", StringComparison.Ordinal)),
            "one header per process opening, not per connection");
        Assert.IsTrue(lines.Any(l => l.EndsWith($" {first.Tag} open cdp-ws a:1", StringComparison.Ordinal)));
        Assert.IsTrue(lines.Any(l => l.EndsWith($" {second.Tag} open cdp-ws b:2", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task File_CompleteAfterTransportDispose()
    {
        var path = Path.Combine(_dir, "nested", "p.log");
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket, log: ProtocolLog.Open(new ProtocolLogOptions { Path = path }, "cdp-ws", "x:1"));
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        for (var i = 1; i <= 50; i++)
        {
            socket.QueueResponse("""{"id":0,"result":{}}""");
            await transport.SendRawAsync("Runtime.evaluate", CdpTransport.EmptyJsonElement(), null, CancellationToken.None);
        }

        await transport.DisposeAsync();

        var lines = File.ReadAllLines(path);
        Assert.AreEqual(50, lines.Count(l => l.Contains(" -> #", StringComparison.Ordinal)));
        Assert.AreEqual(50, lines.Count(l => l.Contains(" <- #", StringComparison.Ordinal)));
        Assert.IsTrue(lines[^1].EndsWith(" closed by Motus", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Append_WritesPidHeader_PerOpening()
    {
        var path = Path.Combine(_dir, "p.log");

        await ProtocolLog.Open(new ProtocolLogOptions { Path = path }, "cdp-ws", "a:1")!.DisposeAsync();
        await ProtocolLog.Open(new ProtocolLogOptions { Path = path }, "cdp-ws", "b:2")!.DisposeAsync();

        var lines = File.ReadAllLines(path);
        Assert.AreEqual(4, lines.Length, string.Join("\n", lines));
        StringAssert.StartsWith(lines[0], $"# motus protocol log, pid {Environment.ProcessId}, ");
        StringAssert.EndsWith(lines[1], "open cdp-ws a:1");
        StringAssert.StartsWith(lines[2], "# motus protocol log, pid ");
        StringAssert.EndsWith(lines[3], "open cdp-ws b:2");
    }

    [TestMethod]
    public async Task TwoWritersSamePath_AppendWithoutOverwriting()
    {
        // Two handles on one file, as two processes sharing a path have, or one process whose last
        // connection to a file is still draining when the next one opens it. Each writes from its
        // own thread; with offsets tracked per handle, each would write over the other's lines.
        var path = Path.Combine(_dir, "p.log");
        const int perWriter = 2000;

        using var first = ProtocolLogFile.Open(path);
        using var second = ProtocolLogFile.Open(path);

        static void Fill(ProtocolLogFile file, string name)
        {
            for (var i = 0; i < perWriter; i++)
                file.Append(Encoding.UTF8.GetBytes($"{name} line {i:D4} of the protocol log\n"));
        }

        await Task.WhenAll(Task.Run(() => Fill(first, "a")), Task.Run(() => Fill(second, "b")));

        var lines = File.ReadAllLines(path);
        Assert.AreEqual(2 * perWriter, lines.Length);
        foreach (var name in new[] { "a", "b" })
        {
            var mine = lines.Where(l => l.StartsWith(name + " ", StringComparison.Ordinal)).ToArray();
            CollectionAssert.AreEqual(
                Enumerable.Range(0, perWriter).Select(i => $"{name} line {i:D4} of the protocol log").ToArray(), mine,
                "every line of one writer arrives whole and in order");
        }
    }

    [TestMethod]
    public void MutexName_IsTheSameForOnePath_AndValidAsAName()
    {
        var path = Path.GetFullPath(Path.Combine(_dir, "p.log"));

        Assert.AreEqual(ProtocolLogFile.MutexName(path), ProtocolLogFile.MutexName(path));
        Assert.AreNotEqual(ProtocolLogFile.MutexName(path), ProtocolLogFile.MutexName(path + "2"));
        StringAssert.Matches(ProtocolLogFile.MutexName(path), new Regex("^Motus\\.ProtocolLog\\.[0-9A-F]{32}$"));
    }

    [TestMethod]
    public async Task UnixFileMode_IsOwnerOnly()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows files take the ACL of their directory.");
            return;
        }

        var path = Path.Combine(_dir, "p.log");
        await ProtocolLog.Open(new ProtocolLogOptions { Path = path }, "cdp-ws", "a:1")!.DisposeAsync();

        Assert.AreEqual(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [TestMethod]
    public void UnopenablePath_WarnsOnStderr_DoesNotThrow()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "a-file");
        File.WriteAllText(blocker, "");
        var warnings = new StringWriter();

        var log = ProtocolLog.Open(
            new ProtocolLogOptions { Path = Path.Combine(blocker, "p.log") }, "cdp-ws", "a:1", warnings);

        Assert.IsNull(log);
        StringAssert.Contains(warnings.ToString(), "Motus: the protocol log could not open");
        StringAssert.Contains(warnings.ToString(), "logging is off");
    }

    [TestMethod]
    public async Task UnopenablePath_WithSink_StillLogsToSink()
    {
        Directory.CreateDirectory(_dir);
        var blocker = Path.Combine(_dir, "a-file");
        File.WriteAllText(blocker, "");
        var lines = new List<string>();

        var log = ProtocolLog.Open(
            new ProtocolLogOptions { Path = Path.Combine(blocker, "p.log"), Sink = lines.Add },
            "cdp-ws", "a:1", new StringWriter());

        Assert.IsNotNull(log);
        await log.DisposeAsync();
        Assert.AreEqual(1, lines.Count);
    }

    [TestMethod]
    public async Task PathAndSink_BothReceiveEveryLine()
    {
        var path = Path.Combine(_dir, "p.log");
        var lines = new List<string>();

        var log = ProtocolLog.Open(new ProtocolLogOptions { Path = path, Sink = lines.Add }, "cdp-ws", "a:1")!;
        log.Closed("by Motus");
        await log.DisposeAsync();

        CollectionAssert.AreEqual(lines, File.ReadAllLines(path).Skip(1).ToList());
        Assert.IsFalse(lines[0].EndsWith('\n'), "a sink is handed the line without its newline");
    }

    [TestMethod]
    public async Task ConnectFailure_ReleasesTheLogFile()
    {
        var path = Path.Combine(_dir, "p.log");

        // A port nothing listens on, so the WebSocket is refused without a browser being involved.
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        await Assert.ThrowsExceptionAsync<System.Net.WebSockets.WebSocketException>(() =>
            MotusLauncher.ConnectAsync(
                $"ws://127.0.0.1:{port}/devtools/browser/00000000-guid-that-grants-control",
                new ConnectOptions { Timeout = 5_000, ProtocolLog = new ProtocolLogOptions { Path = path } }));

        Assert.IsFalse(ProtocolLogSink.IsOpen(path), "a failed connect lets go of its log");

        var text = File.ReadAllText(path);
        StringAssert.Contains(text, $"open cdp-ws 127.0.0.1:{port}\n");
        Assert.IsFalse(text.Contains("devtools", StringComparison.Ordinal), "the endpoint path is never logged");
    }
}
