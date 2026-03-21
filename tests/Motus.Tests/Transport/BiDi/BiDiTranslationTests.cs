using System.Text.Json;

namespace Motus.Tests.Transport.BiDi;

[TestClass]
public class BiDiTranslationTests
{
    // ──────────────────────────────────────────
    // Registry lookup
    // ──────────────────────────────────────────

    [TestMethod]
    public void Registry_Returns_Translation_For_Known_Method()
    {
        Assert.IsTrue(BiDiTranslationRegistry.TryGet("Page.navigate", out var translation));
        Assert.IsNotNull(translation);
        Assert.AreEqual("browsingContext.navigate", translation.BiDiMethod);
    }

    [TestMethod]
    public void Registry_Returns_False_For_Unknown_Method()
    {
        Assert.IsFalse(BiDiTranslationRegistry.TryGet("Unknown.method", out _));
    }

    // ──────────────────────────────────────────
    // Browser / Session
    // ──────────────────────────────────────────

    [TestMethod]
    public void BrowserGetVersion_Translates_Result()
    {
        var translation = GetTranslation("Browser.getVersion");

        var bidiResult = Parse("""{"ready":true,"message":"Firefox/130.0"}""");
        var cdpResult = translation.TranslateResult(bidiResult);

        Assert.AreEqual("Firefox/130.0", cdpResult.GetProperty("product").GetString());
        Assert.AreEqual("Firefox/130.0", cdpResult.GetProperty("userAgent").GetString());
    }

    [TestMethod]
    public void BrowserClose_Returns_Empty()
    {
        var translation = GetTranslation("Browser.close");
        var result = translation.TranslateResult(JsonBuilder.Empty());
        Assert.AreEqual(JsonValueKind.Object, result.ValueKind);
    }

    // ──────────────────────────────────────────
    // Target management
    // ──────────────────────────────────────────

    [TestMethod]
    public void TargetCreateTarget_Maps_Context_To_TargetId()
    {
        var translation = GetTranslation("Target.createTarget");

        var bidiResult = Parse("""{"context":"ctx-abc"}""");
        var cdpResult = translation.TranslateResult(bidiResult);

        Assert.AreEqual("ctx-abc", cdpResult.GetProperty("targetId").GetString());
    }

    [TestMethod]
    public void TargetCloseTarget_Maps_TargetId_To_Context()
    {
        var translation = GetTranslation("Target.closeTarget");

        var cdpParams = Parse("""{"targetId":"ctx-1"}""");
        var bidiParams = translation.TranslateParams(cdpParams, null);

        Assert.AreEqual("ctx-1", bidiParams.GetProperty("context").GetString());
    }

    [TestMethod]
    public void TargetAttachToTarget_Returns_SessionId()
    {
        var translation = GetTranslation("Target.attachToTarget");
        var result = translation.TranslateResult(JsonBuilder.Empty());
        Assert.IsNotNull(result.GetProperty("sessionId").GetString());
    }

    [TestMethod]
    public void TargetCreateBrowserContext_Returns_Synthetic_Id()
    {
        var translation = GetTranslation("Target.createBrowserContext");
        var result = translation.TranslateResult(JsonBuilder.Empty());
        var id = result.GetProperty("browserContextId").GetString();
        Assert.IsNotNull(id);
        StringAssert.StartsWith(id, "bidi-ctx-");
    }

    // ──────────────────────────────────────────
    // Page / Navigation
    // ──────────────────────────────────────────

    [TestMethod]
    public void PageNavigate_Translates_Params_And_Result()
    {
        var translation = GetTranslation("Page.navigate");

        var cdpParams = Parse("""{"url":"https://example.com"}""");
        var bidiParams = translation.TranslateParams(cdpParams, "ctx-1");

        Assert.AreEqual("ctx-1", bidiParams.GetProperty("context").GetString());
        Assert.AreEqual("https://example.com", bidiParams.GetProperty("url").GetString());
        Assert.AreEqual("complete", bidiParams.GetProperty("wait").GetString());

        var bidiResult = Parse("""{"navigation":"nav-1","url":"https://example.com"}""");
        var cdpResult = translation.TranslateResult(bidiResult);

        Assert.AreEqual("https://example.com", cdpResult.GetProperty("frameId").GetString());
        Assert.AreEqual("nav-1", cdpResult.GetProperty("loaderId").GetString());
    }

    [TestMethod]
    public void PageReload_Maps_Context()
    {
        var translation = GetTranslation("Page.reload");

        var bidiParams = translation.TranslateParams(JsonBuilder.Empty(), "ctx-1");
        Assert.AreEqual("ctx-1", bidiParams.GetProperty("context").GetString());
        Assert.AreEqual("complete", bidiParams.GetProperty("wait").GetString());
    }

    // ──────────────────────────────────────────
    // Script
    // ──────────────────────────────────────────

    [TestMethod]
    public void RuntimeEvaluate_Translates_Params()
    {
        var translation = GetTranslation("Runtime.evaluate");

        var cdpParams = Parse("""{"expression":"1+1","returnByValue":true,"awaitPromise":false}""");
        var bidiParams = translation.TranslateParams(cdpParams, "ctx-1");

        Assert.AreEqual("1+1", bidiParams.GetProperty("expression").GetString());
        Assert.AreEqual(false, bidiParams.GetProperty("awaitPromise").GetBoolean());
        Assert.AreEqual("ctx-1", bidiParams.GetProperty("target").GetProperty("context").GetString());
    }

    [TestMethod]
    public void RuntimeEvaluate_Success_Result_Maps_To_RemoteObject()
    {
        var translation = GetTranslation("Runtime.evaluate");

        var bidiResult = Parse("""{"type":"success","result":{"type":"number","value":2}}""");
        var cdpResult = translation.TranslateResult(bidiResult);

        var remoteObj = cdpResult.GetProperty("result");
        Assert.AreEqual("number", remoteObj.GetProperty("type").GetString());
        Assert.AreEqual(2, remoteObj.GetProperty("value").GetInt32());
    }

    [TestMethod]
    public void RuntimeEvaluate_Exception_Result_Has_ExceptionDetails()
    {
        var translation = GetTranslation("Runtime.evaluate");

        var bidiResult = Parse("""{"type":"exception","exceptionDetails":{"text":"ReferenceError","columnNumber":0,"lineNumber":0,"exception":{"type":"object"}}}""");
        var cdpResult = translation.TranslateResult(bidiResult);

        Assert.IsTrue(cdpResult.TryGetProperty("exceptionDetails", out var details));
        Assert.AreEqual("ReferenceError", details.GetProperty("text").GetString());
    }

    [TestMethod]
    public void RuntimeCallFunctionOn_Translates_Params()
    {
        var translation = GetTranslation("Runtime.callFunctionOn");

        var cdpParams = Parse("""{"functionDeclaration":"function(){return 1;}","awaitPromise":true,"returnByValue":true}""");
        var bidiParams = translation.TranslateParams(cdpParams, "ctx-1");

        Assert.AreEqual("function(){return 1;}", bidiParams.GetProperty("functionDeclaration").GetString());
        Assert.AreEqual(true, bidiParams.GetProperty("awaitPromise").GetBoolean());
    }

    [TestMethod]
    public void RuntimeReleaseObject_Is_NoOp()
    {
        var translation = GetTranslation("Runtime.releaseObject");
        var result = translation.TranslateResult(JsonBuilder.Empty());
        Assert.AreEqual(JsonValueKind.Object, result.ValueKind);
    }

    // ──────────────────────────────────────────
    // Input
    // ──────────────────────────────────────────

    [TestMethod]
    public void InputDispatchKeyEvent_KeyDown_Creates_Action_Sequence()
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = Parse("""{"type":"rawKeyDown","key":"a","code":"KeyA"}""");
        var bidiParams = translation.TranslateParams(cdpParams, "ctx-1");

        Assert.AreEqual("ctx-1", bidiParams.GetProperty("context").GetString());
        var actions = bidiParams.GetProperty("actions");
        Assert.AreEqual(1, actions.GetArrayLength());
        Assert.AreEqual("key", actions[0].GetProperty("type").GetString());
        Assert.AreEqual("keyDown", actions[0].GetProperty("actions")[0].GetProperty("type").GetString());
        Assert.AreEqual("a", actions[0].GetProperty("actions")[0].GetProperty("value").GetString());
    }

    [TestMethod]
    public void InputDispatchKeyEvent_KeyUp_Creates_KeyUp_Action()
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = Parse("""{"type":"keyUp","key":"a"}""");
        var bidiParams = translation.TranslateParams(cdpParams, "ctx-1");

        var action = bidiParams.GetProperty("actions")[0].GetProperty("actions")[0];
        Assert.AreEqual("keyUp", action.GetProperty("type").GetString());
    }

    [TestMethod]
    public void InputDispatchMouseEvent_MousePressed_Creates_Pointer_Sequence()
    {
        var translation = GetTranslation("Input.dispatchMouseEvent");

        var cdpParams = Parse("""{"type":"mousePressed","x":100,"y":200,"button":"left","clickCount":1}""");
        var bidiParams = translation.TranslateParams(cdpParams, "ctx-1");

        var seq = bidiParams.GetProperty("actions")[0];
        Assert.AreEqual("pointer", seq.GetProperty("type").GetString());

        var actions = seq.GetProperty("actions");
        // Should have pointerMove then pointerDown
        Assert.AreEqual(2, actions.GetArrayLength());
        Assert.AreEqual("pointerMove", actions[0].GetProperty("type").GetString());
        Assert.AreEqual(100, actions[0].GetProperty("x").GetDouble());
        Assert.AreEqual("pointerDown", actions[1].GetProperty("type").GetString());
    }

    [TestMethod]
    public void InputDispatchTouchEvent_Creates_Touch_Pointer_Sequence()
    {
        var translation = GetTranslation("Input.dispatchTouchEvent");

        var cdpParams = Parse("""{"type":"touchStart","touchPoints":[{"x":50,"y":60}]}""");
        var bidiParams = translation.TranslateParams(cdpParams, "ctx-1");

        var seq = bidiParams.GetProperty("actions")[0];
        Assert.AreEqual("pointer", seq.GetProperty("type").GetString());
        Assert.AreEqual("touch", seq.GetProperty("parameters").GetProperty("pointerType").GetString());
    }

    // ──────────────────────────────────────────
    // Network
    // ──────────────────────────────────────────

    [TestMethod]
    public void FetchEnable_Creates_AddIntercept()
    {
        var translation = GetTranslation("Fetch.enable");
        Assert.AreEqual("network.addIntercept", translation.BiDiMethod);

        var bidiParams = translation.TranslateParams(JsonBuilder.Empty(), "ctx-1");
        Assert.IsTrue(bidiParams.TryGetProperty("phases", out var phases));
        Assert.AreEqual("beforeRequestSent", phases[0].GetString());
    }

    [TestMethod]
    public void FetchContinueRequest_Maps_RequestId()
    {
        var translation = GetTranslation("Fetch.continueRequest");

        var cdpParams = Parse("""{"requestId":"req-1","url":"https://modified.com"}""");
        var bidiParams = translation.TranslateParams(cdpParams, null);

        Assert.AreEqual("req-1", bidiParams.GetProperty("request").GetString());
        Assert.AreEqual("https://modified.com", bidiParams.GetProperty("url").GetString());
    }

    [TestMethod]
    public void FetchFulfillRequest_Maps_Status_And_Body()
    {
        var translation = GetTranslation("Fetch.fulfillRequest");

        var cdpParams = Parse("""{"requestId":"req-1","responseCode":200,"body":"dGVzdA=="}""");
        var bidiParams = translation.TranslateParams(cdpParams, null);

        Assert.AreEqual("req-1", bidiParams.GetProperty("request").GetString());
        Assert.AreEqual(200, bidiParams.GetProperty("statusCode").GetInt32());
        Assert.AreEqual("base64", bidiParams.GetProperty("body").GetProperty("type").GetString());
    }

    [TestMethod]
    public void FetchFailRequest_Maps_RequestId()
    {
        var translation = GetTranslation("Fetch.failRequest");

        var cdpParams = Parse("""{"requestId":"req-1","errorReason":"Failed"}""");
        var bidiParams = translation.TranslateParams(cdpParams, null);

        Assert.AreEqual("req-1", bidiParams.GetProperty("request").GetString());
    }

    // ──────────────────────────────────────────
    // No-op / Stubbed
    // ──────────────────────────────────────────

    [TestMethod]
    [DataRow("Page.enable")]
    [DataRow("DOM.enable")]
    [DataRow("Network.enable")]
    [DataRow("Runtime.enable")]
    [DataRow("Target.setAutoAttach")]
    [DataRow("Target.disposeBrowserContext")]
    public void Stubbed_Translations_Return_Empty_Result(string cdpMethod)
    {
        Assert.IsTrue(BiDiTranslationRegistry.TryGet(cdpMethod, out var translation));
        var result = translation!.TranslateResult(JsonBuilder.Empty());
        Assert.AreEqual(JsonValueKind.Object, result.ValueKind);
    }

    // ──────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────

    private static IBiDiTranslation GetTranslation(string cdpMethod)
    {
        Assert.IsTrue(BiDiTranslationRegistry.TryGet(cdpMethod, out var translation),
            $"No translation found for {cdpMethod}");
        return translation!;
    }

    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }
}
