using System.Text.Json;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Locator;

/// <summary>
/// Verifies the on-disk lifetime of upload payloads. The browser stores the
/// staged file paths and reads the bytes lazily, possibly long after the
/// upload action returns (a change handler awaiting file.arrayBuffer(), or a
/// framework streaming the file to a server). The staged files must therefore
/// survive the action and only be removed when the page is disposed.
/// </summary>
[TestClass]
public class LocatorSetInputFilesTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
        _browser = new Motus.Browser(_transport, _registry, process: null, tempUserDataDir: null,
                                     handleSigint: false, handleSigterm: false);
        var initTask = _browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    private async Task<IPage> CreatePageAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        return await _browser.NewPageAsync();
    }

    /// <summary>
    /// Queues the responses for SetInputFilesAsync: strategy resolve (evaluate +
    /// getProperties), visible check, enabled check, then DOM.setFileInputFiles.
    /// </summary>
    private void QueueSetInputFilesResponses(int startId, string objectId = "input-1")
    {
        var id = startId;
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-{objectId}""}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""0"", ""value"": {{""type"": ""object"", ""objectId"": ""{objectId}""}}}}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 1}}}}]}}}}");
        // visible
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // DOM.setFileInputFiles
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
    }

    private List<string> GetSetFileInputFilesPaths()
    {
        var paths = new List<string>();
        for (var i = 0; i < _socket.SentMessages.Count; i++)
        {
            using var doc = JsonDocument.Parse(_socket.GetSentJson(i));
            if (doc.RootElement.TryGetProperty("method", out var method) &&
                method.GetString() == "DOM.setFileInputFiles")
            {
                paths.Add(doc.RootElement.GetProperty("params").GetProperty("files")[0].GetString()!);
            }
        }

        return paths;
    }

    [TestMethod]
    public async Task SetInputFilesAsync_BackingFileSurvivesTheAction()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("input[type=file]");
        QueueSetInputFilesResponses(9);

        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        await locator.SetInputFilesAsync([new FilePayload("upload.bin", "application/octet-stream", bytes)]);

        var sentPaths = GetSetFileInputFilesPaths();
        Assert.AreEqual(1, sentPaths.Count, "Expected one DOM.setFileInputFiles message.");
        var stagedPath = sentPaths[0];
        Assert.IsTrue(File.Exists(stagedPath),
            "The staged upload file must remain on disk after the action returns, " +
            "because the browser reads it lazily.");
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(stagedPath));

        await page.DisposeAsync();
        Assert.IsFalse(File.Exists(stagedPath),
            "The staged upload file must be removed when the page is disposed.");
    }

    [TestMethod]
    public async Task SetInputFilesAsync_SameNamedUploadsDoNotCollide()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("input[type=file]");

        QueueSetInputFilesResponses(9);
        await locator.SetInputFilesAsync([new FilePayload("same.txt", "text/plain", [1])]);

        QueueSetInputFilesResponses(14);
        await locator.SetInputFilesAsync([new FilePayload("same.txt", "text/plain", [2, 2])]);

        // Both staged files exist with distinct paths and their own content.
        var paths = GetSetFileInputFilesPaths();
        Assert.AreEqual(2, paths.Count);
        Assert.AreNotEqual(paths[0], paths[1]);
        Assert.AreEqual(1, (await File.ReadAllBytesAsync(paths[0])).Length);
        Assert.AreEqual(2, (await File.ReadAllBytesAsync(paths[1])).Length);

        await page.DisposeAsync();
        Assert.IsFalse(File.Exists(paths[0]));
        Assert.IsFalse(File.Exists(paths[1]));
    }
}
