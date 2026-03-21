using System.Text.Json;

namespace Motus.Tests.Transport.BiDi;

[TestClass]
public class BiDiEventMapTests
{
    // ──────────────────────────────────────────
    // Name mapping
    // ──────────────────────────────────────────

    [TestMethod]
    [DataRow("Page.loadEventFired", "browsingContext.load")]
    [DataRow("Page.domContentEventFired", "browsingContext.domContentLoaded")]
    [DataRow("Page.frameNavigated", "browsingContext.navigationStarted")]
    [DataRow("Page.javascriptDialogOpening", "browsingContext.userPromptOpened")]
    [DataRow("Page.javascriptDialogClosed", "browsingContext.userPromptClosed")]
    [DataRow("Fetch.requestPaused", "network.beforeRequestSent")]
    [DataRow("Network.requestWillBeSent", "network.beforeRequestSent")]
    [DataRow("Network.responseReceived", "network.responseCompleted")]
    [DataRow("Target.attachedToTarget", "browsingContext.contextCreated")]
    [DataRow("Target.detachedFromTarget", "browsingContext.contextDestroyed")]
    [DataRow("Runtime.consoleAPICalled", "log.entryAdded")]
    public void CdpEvent_Maps_To_BiDiEvent(string cdpEvent, string expectedBiDiEvent)
    {
        var biDiName = BiDiEventMap.ToBiDiEventName(cdpEvent);
        Assert.AreEqual(expectedBiDiEvent, biDiName);
    }

    [TestMethod]
    public void Unknown_CdpEvent_Returns_Null()
    {
        Assert.IsNull(BiDiEventMap.ToBiDiEventName("Unknown.event"));
        Assert.IsNull(BiDiEventMap.GetEventTranslation("Unknown.event"));
    }

    // ──────────────────────────────────────────
    // Event payload translation
    // ──────────────────────────────────────────

    [TestMethod]
    public void LoadEventFired_Extracts_Timestamp()
    {
        var translation = BiDiEventMap.GetEventTranslation("Page.loadEventFired")!;
        var bidiParams = Parse("""{"context":"ctx-1","timestamp":12345}""");
        var cdpParams = translation.TranslateEvent(bidiParams);

        Assert.AreEqual(12345, cdpParams.GetProperty("timestamp").GetDouble());
    }

    [TestMethod]
    public void FrameNavigated_Builds_Frame_Object()
    {
        var translation = BiDiEventMap.GetEventTranslation("Page.frameNavigated")!;
        var bidiParams = Parse("""{"context":"ctx-1","navigation":"nav-1","url":"https://example.com","timestamp":1}""");
        var cdpParams = translation.TranslateEvent(bidiParams);

        var frame = cdpParams.GetProperty("frame");
        Assert.AreEqual("ctx-1", frame.GetProperty("id").GetString());
        Assert.AreEqual("https://example.com", frame.GetProperty("url").GetString());
    }

    [TestMethod]
    public void DialogOpening_Maps_Type_And_Message()
    {
        var translation = BiDiEventMap.GetEventTranslation("Page.javascriptDialogOpening")!;
        var bidiParams = Parse("""{"context":"ctx-1","type":"confirm","message":"Are you sure?"}""");
        var cdpParams = translation.TranslateEvent(bidiParams);

        Assert.AreEqual("confirm", cdpParams.GetProperty("type").GetString());
        Assert.AreEqual("Are you sure?", cdpParams.GetProperty("message").GetString());
    }

    [TestMethod]
    public void DialogClosed_Maps_Accepted_And_UserText()
    {
        var translation = BiDiEventMap.GetEventTranslation("Page.javascriptDialogClosed")!;
        var bidiParams = Parse("""{"context":"ctx-1","accepted":true,"userText":"hello"}""");
        var cdpParams = translation.TranslateEvent(bidiParams);

        Assert.AreEqual(true, cdpParams.GetProperty("result").GetBoolean());
        Assert.AreEqual("hello", cdpParams.GetProperty("userInput").GetString());
    }

    [TestMethod]
    public void RequestPaused_Maps_Request_Fields()
    {
        var translation = BiDiEventMap.GetEventTranslation("Fetch.requestPaused")!;
        var bidiParams = Parse("""{"context":"ctx-1","request":{"request":"req-1","url":"https://api.com","method":"POST"}}""");
        var cdpParams = translation.TranslateEvent(bidiParams);

        Assert.AreEqual("req-1", cdpParams.GetProperty("requestId").GetString());
        Assert.AreEqual("ctx-1", cdpParams.GetProperty("frameId").GetString());
        var request = cdpParams.GetProperty("request");
        Assert.AreEqual("https://api.com", request.GetProperty("url").GetString());
        Assert.AreEqual("POST", request.GetProperty("method").GetString());
    }

    [TestMethod]
    public void AttachedToTarget_Maps_Context_To_SessionId()
    {
        var translation = BiDiEventMap.GetEventTranslation("Target.attachedToTarget")!;
        var bidiParams = Parse("""{"context":"ctx-new","url":"about:blank"}""");
        var cdpParams = translation.TranslateEvent(bidiParams);

        Assert.AreEqual("ctx-new", cdpParams.GetProperty("sessionId").GetString());
        Assert.AreEqual("ctx-new", cdpParams.GetProperty("targetInfo").GetProperty("targetId").GetString());
    }

    [TestMethod]
    public void ConsoleApiCalled_Maps_Level_And_Text()
    {
        var translation = BiDiEventMap.GetEventTranslation("Runtime.consoleAPICalled")!;
        var bidiParams = Parse("""{"level":"error","text":"something broke","timestamp":99999}""");
        var cdpParams = translation.TranslateEvent(bidiParams);

        Assert.AreEqual("error", cdpParams.GetProperty("type").GetString());
        var firstArg = cdpParams.GetProperty("args")[0];
        Assert.AreEqual("something broke", firstArg.GetProperty("value").GetString());
    }

    // ──────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
