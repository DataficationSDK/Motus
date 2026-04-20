using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Locator;

[TestClass]
public class LocatorEvaluateWithElementTests
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

    private void QueueBaseResolve(ref int id, string objectId)
    {
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-{objectId}""}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""0"", ""value"": {{""type"": ""object"", ""objectId"": ""{objectId}""}}}}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 1}}}}]}}}}");
    }

    [TestMethod]
    public async Task EvaluateWithElement_ArrowNoArg_PassesElementAsFirstArg()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#target");

        var id = 9;
        QueueBaseResolve(ref id, "elem-1");

        // callFunctionOn: returns the string result
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""rgb(0, 0, 0)""}}}}}}");

        var result = await locator.EvaluateWithElementAsync<string>("el => getComputedStyle(el).backgroundColor");
        Assert.AreEqual("rgb(0, 0, 0)", result);

        // The callFunctionOn call (third sent message in this suite, 2 from resolve + 1 eval) should
        // carry the element's objectId both as the call target and in the first slot of Arguments.
        var callFunctionOn = _socket.GetSentJson(_socket.SentMessages.Count - 1);
        StringAssert.Contains(callFunctionOn, "Runtime.callFunctionOn", StringComparison.Ordinal);
        StringAssert.Contains(callFunctionOn, "\"objectId\":\"elem-1\"", StringComparison.Ordinal);
        // Arguments should be an array beginning with an objectId reference to the element.
        StringAssert.Contains(callFunctionOn, "\"arguments\":[{\"objectId\":\"elem-1\"}]", StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task EvaluateWithElement_WithArg_PassesElementThenArg()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#target");

        var id = 9;
        QueueBaseResolve(ref id, "elem-1");

        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""rgb(255, 0, 0)""}}}}}}");

        var result = await locator.EvaluateWithElementAsync<string>(
            "(el, prop) => getComputedStyle(el).getPropertyValue(prop)",
            "background-color");
        Assert.AreEqual("rgb(255, 0, 0)", result);

        var callFunctionOn = _socket.GetSentJson(_socket.SentMessages.Count - 1);
        // Arguments: [{objectId: "elem-1"}, {value: "background-color"}]
        StringAssert.Contains(callFunctionOn, "\"objectId\":\"elem-1\"", StringComparison.Ordinal);
        StringAssert.Contains(callFunctionOn, "\"value\":\"background-color\"", StringComparison.Ordinal);
        // Element must come before the user value.
        var objectIdIdx = callFunctionOn.IndexOf("\"arguments\":[{\"objectId\":\"elem-1\"}", StringComparison.Ordinal);
        Assert.IsTrue(objectIdIdx >= 0, "element objectId should lead the Arguments array");
    }

    [TestMethod]
    public async Task EvaluateWithElement_FunctionLiteral_AlsoReceivesElementAsFirstParam()
    {
        // A classic function() {} literal also works — it receives the element as its first named
        // parameter instead of via `this`.
        var page = await CreatePageAsync();
        var locator = page.Locator("#target");

        var id = 9;
        QueueBaseResolve(ref id, "elem-1");

        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""Hello""}}}}}}");

        var result = await locator.EvaluateWithElementAsync<string>(
            "function(el) { return el.textContent; }");
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public async Task EvaluateAsync_StillBindsThis_BackwardCompat()
    {
        // Regression guard: the existing EvaluateAsync behavior (this-binding, user arg in slot 1)
        // must be preserved exactly.
        var page = await CreatePageAsync();
        var locator = page.Locator("#target");

        var id = 9;
        QueueBaseResolve(ref id, "elem-1");

        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""the-id""}}}}}}");

        var result = await locator.EvaluateAsync<string>(
            "function(name) { return this.getAttribute(name); }",
            "id");
        Assert.AreEqual("the-id", result);

        var callFunctionOn = _socket.GetSentJson(_socket.SentMessages.Count - 1);
        // Legacy shape: Arguments = [{value: "id"}], element is ObjectId on the call itself, not an arg.
        StringAssert.Contains(callFunctionOn, "\"arguments\":[{\"value\":\"id\"}]", StringComparison.Ordinal);
    }
}
