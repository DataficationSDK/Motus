using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Stress;

/// <summary>
/// Wall-clock regression smoke test for the DOM-scoped chained locator path.
/// Resolves a 500-row table's <c>.row .cell</c> chain under a method-aware fake
/// socket so the measurement captures the Motus resolution/allocation overhead
/// rather than real browser round-trips. The absolute budget is generous so the
/// test signals pathological regressions (for example, accidental O(n^2)
/// behavior) rather than ambient CI machine variance.
/// </summary>
[TestClass]
[TestCategory("Stress")]
[TestCategory("Benchmark")]
public class LocatorScopingBenchmark
{
    private const int RowCount = 500;
    private const int BudgetMs = 5_000;

    private MethodAwareFakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new MethodAwareFakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
        _browser = new Motus.Browser(_transport, _registry, process: null, tempUserDataDir: null,
                                     handleSigint: false, handleSigterm: false);
        _socket.Respond(id: 1, @"{""id"": 1, ""result"": {""protocolVersion"":""1.3"",""product"":""Chrome/120"",""revision"":""@x"",""userAgent"":""UA"",""jsVersion"":""12""}}");
        await _browser.InitializeAsync(CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    private async Task<IPage> CreatePageAsync()
    {
        _socket.Respond(2, @"{""id"": 2, ""result"": {""browserContextId"": ""ctx-1""}}");
        _socket.Respond(3, @"{""id"": 3, ""result"": {""targetId"": ""target-1""}}");
        _socket.Respond(4, @"{""id"": 4, ""result"": {""sessionId"": ""session-1""}}");
        _socket.Respond(5, @"{""id"": 5, ""sessionId"": ""session-1"", ""result"": {}}");
        _socket.Respond(6, @"{""id"": 6, ""sessionId"": ""session-1"", ""result"": {}}");
        _socket.Respond(7, @"{""id"": 7, ""sessionId"": ""session-1"", ""result"": {}}");
        _socket.Respond(8, @"{""id"": 8, ""sessionId"": ""session-1"", ""result"": {}}");
        return await _browser.NewPageAsync();
    }

    [TestMethod]
    public async Task ScopedChain_500Rows_ResolvesWithinBudget()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator(".row").Locator(".cell");

        // Respond to whatever ids the transport assigns — not FIFO — so the 500
        // parallel descendant queries don't get content-shape mismatches when the
        // threadpool interleaves cFO sends from the outer loop with getProps
        // sends from cFO continuations.
        _socket.SetHandler(envelope =>
        {
            var id = envelope.GetProperty("id").GetInt32();
            var method = envelope.GetProperty("method").GetString()!;

            return method switch
            {
                // Strategy resolve for `.row` returns the 500 row element handles.
                "Runtime.evaluate" => BuildBaseEval(id),
                // The base's Runtime.getProperties (for the row NodeList) returns 500 row elements;
                // each descendant query's Runtime.getProperties returns a single cell element. The
                // socket disambiguates by inspecting the requested objectId.
                "Runtime.getProperties" => BuildGetProperties(id, envelope),
                // Descendant query: Runtime.callFunctionOn returns a NodeList object id tied to the
                // parent's row index so the subsequent getProperties can resolve to a unique cell.
                "Runtime.callFunctionOn" => BuildCallFunctionOn(id, envelope),
                _ => throw new InvalidOperationException($"Unexpected CDP method in benchmark: {method}"),
            };
        });

        var sw = Stopwatch.StartNew();
        var count = await locator.CountAsync();
        sw.Stop();

        Assert.AreEqual(RowCount, count, "Every row should contribute exactly one distinct cell.");
        Console.WriteLine($"ScopedChain_{RowCount}Rows wall-clock: {sw.ElapsedMilliseconds} ms");
        Assert.IsTrue(sw.ElapsedMilliseconds < BudgetMs,
            $"Scoped chain resolution for {RowCount} rows took {sw.ElapsedMilliseconds} ms, budget {BudgetMs} ms.");
    }

    private static string BuildBaseEval(int id)
        => @"{""id"": " + id + @", ""sessionId"": ""session-1"", ""result"": {""result"": {""type"": ""object"", ""objectId"": ""arr-rows""}}}";

    private static string BuildGetProperties(int id, JsonElement envelope)
    {
        var targetObjectId = envelope.GetProperty("params").GetProperty("objectId").GetString()!;
        if (targetObjectId == "arr-rows")
        {
            var rowItems = string.Join(", ", Enumerable.Range(0, RowCount)
                .Select(i => @"{""name"": """ + i + @""", ""value"": {""type"": ""object"", ""objectId"": ""row-" + i + @"""}}"));
            return @"{""id"": " + id + @", ""sessionId"": ""session-1"", ""result"": {""result"": [" + rowItems + @", {""name"": ""length"", ""value"": {""type"": ""number"", ""value"": " + RowCount + @"}}]}}";
        }

        // targetObjectId is "arr-cells-N" for some N in 0..499.
        var suffix = targetObjectId["arr-cells-".Length..];
        return @"{""id"": " + id + @", ""sessionId"": ""session-1"", ""result"": {""result"": [{""name"": ""0"", ""value"": {""type"": ""object"", ""objectId"": ""cell-" + suffix + @"""}}, {""name"": ""length"", ""value"": {""type"": ""number"", ""value"": 1}}]}}";
    }

    private static string BuildCallFunctionOn(int id, JsonElement envelope)
    {
        var parentObjectId = envelope.GetProperty("params").GetProperty("objectId").GetString()!;
        // Parent objectId is "row-N"; map to "arr-cells-N".
        var suffix = parentObjectId["row-".Length..];
        return @"{""id"": " + id + @", ""sessionId"": ""session-1"", ""result"": {""result"": {""type"": ""object"", ""objectId"": ""arr-cells-" + suffix + @"""}}}";
    }

    /// <summary>
    /// Fake socket that inspects each outbound command envelope and responds via a test-supplied
    /// handler. Unlike the FIFO <see cref="FakeCdpSocket"/>, this decouples response content from
    /// send order, which matters under heavy parallel sends where threadpool interleaving can
    /// assign ids out of the caller's logical order.
    /// </summary>
    private sealed class MethodAwareFakeCdpSocket : ICdpSocket
    {
        private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>();
        private readonly ConcurrentDictionary<int, string> _fixedResponses = new();
        private Func<JsonElement, string>? _handler;

        public bool IsOpen { get; private set; } = true;

        public Task ConnectAsync(Uri endpointUri, CancellationToken ct)
        {
            IsOpen = true;
            return Task.CompletedTask;
        }

        public Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            var id = root.GetProperty("id").GetInt32();

            string response;
            if (_fixedResponses.TryRemove(id, out var fixedResponse))
            {
                response = fixedResponse;
            }
            else
            {
                var handler = _handler
                    ?? throw new InvalidOperationException(
                        $"No handler and no fixed response configured for id {id}.");
                response = handler(root);
            }

            _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(response));
            return Task.CompletedTask;
        }

        public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
        {
            if (!IsOpen) return ReadOnlyMemory<byte>.Empty;
            try { return await _inbox.Reader.ReadAsync(ct); }
            catch (ChannelClosedException) { return ReadOnlyMemory<byte>.Empty; }
        }

        internal void Respond(int id, string json) => _fixedResponses[id] = json;
        internal void SetHandler(Func<JsonElement, string> handler) => _handler = handler;

        public ValueTask DisposeAsync()
        {
            IsOpen = false;
            _inbox.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
