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

    [TestMethod]
    public void PageGetFrameTree_Reads_Only_The_Page_Context()
    {
        var translation = GetTranslation("Page.getFrameTree");

        var bidiParams = translation.TranslateParams(JsonBuilder.Empty(), "ctx-1");
        Assert.AreEqual("browsingContext.getTree", translation.BiDiMethod);
        Assert.AreEqual("ctx-1", bidiParams.GetProperty("root").GetString());
        Assert.AreEqual(0, bidiParams.GetProperty("maxDepth").GetInt32());
    }

    [TestMethod]
    public void PageGetFrameTree_Maps_Root_Context_To_Main_Frame()
    {
        var translation = GetTranslation("Page.getFrameTree");

        var bidiResult = Parse("""
            {"contexts":[{"context":"ctx-1","url":"about:blank","children":null,"userContext":"default"}]}
            """);
        var frame = translation.TranslateResult(bidiResult).GetProperty("frameTree").GetProperty("frame");

        Assert.AreEqual("ctx-1", frame.GetProperty("id").GetString());
        Assert.AreEqual("about:blank", frame.GetProperty("url").GetString());
        Assert.IsFalse(frame.TryGetProperty("parentId", out _));
    }

    [TestMethod]
    public void PageGetFrameTree_No_Contexts_Returns_Empty()
    {
        var translation = GetTranslation("Page.getFrameTree");

        var cdpResult = translation.TranslateResult(Parse("""{"contexts":[]}"""));

        Assert.IsFalse(cdpResult.TryGetProperty("frameTree", out _));
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
    [DataRow("Enter", "Enter", "")]
    [DataRow("Enter", "NumpadEnter", "")]
    [DataRow("Tab", "Tab", "")]
    [DataRow("Escape", "Escape", "")]
    [DataRow("Backspace", "Backspace", "")]
    [DataRow("Delete", "Delete", "")]
    [DataRow("Insert", "Insert", "")]
    [DataRow("Home", "Home", "")]
    [DataRow("End", "End", "")]
    [DataRow("PageUp", "PageUp", "")]
    [DataRow("PageDown", "PageDown", "")]
    [DataRow("ArrowLeft", "ArrowLeft", "")]
    [DataRow("ArrowUp", "ArrowUp", "")]
    [DataRow("ArrowRight", "ArrowRight", "")]
    [DataRow("ArrowDown", "ArrowDown", "")]
    [DataRow("Pause", "Pause", "")]
    [DataRow("F1", "F1", "")]
    [DataRow("F5", "F5", "")]
    [DataRow("F12", "F12", "")]
    [DataRow("Shift", "ShiftLeft", "")]
    [DataRow("Shift", "ShiftRight", "")]
    [DataRow("Control", "ControlLeft", "")]
    [DataRow("Control", "ControlRight", "")]
    [DataRow("Alt", "AltLeft", "")]
    [DataRow("Alt", "AltRight", "")]
    [DataRow("Meta", "MetaLeft", "")]
    [DataRow("Meta", "MetaRight", "")]
    [DataRow("0", "Numpad0", "")]
    [DataRow("5", "Numpad5", "")]
    [DataRow("9", "Numpad9", "")]
    [DataRow("*", "NumpadMultiply", "")]
    [DataRow("+", "NumpadAdd", "")]
    [DataRow("-", "NumpadSubtract", "")]
    [DataRow(".", "NumpadDecimal", "")]
    [DataRow("/", "NumpadDivide", "")]
    [DataRow("Home", "Numpad7", "")]
    [DataRow("Delete", "NumpadDecimal", "")]
    public void InputDispatchKeyEvent_NamedKey_UsesWebDriverCodePoint(string key, string code, string expected)
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        foreach (var type in new[] { "rawKeyDown", "keyUp" })
        {
            var cdpParams = JsonBuilder.FromObject(new { type, key, code });
            var action = translation.TranslateParams(cdpParams, "ctx-1")
                .GetProperty("actions")[0].GetProperty("actions")[0];

            Assert.AreEqual(expected, action.GetProperty("value").GetString(), $"{type} {key} ({code})");
        }
    }

    [TestMethod]
    [DataRow("Enter", "")]
    [DataRow("Shift", "")]
    [DataRow("Control", "")]
    [DataRow("Escape", "")]
    public void InputDispatchKeyEvent_NamedKeyWithoutCode_UsesWebDriverCodePoint(string key, string expected)
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = JsonBuilder.FromObject(new { type = "rawKeyDown", key });
        var action = translation.TranslateParams(cdpParams, "ctx-1")
            .GetProperty("actions")[0].GetProperty("actions")[0];

        Assert.AreEqual(expected, action.GetProperty("value").GetString());
    }

    [TestMethod]
    [DataRow("a", "KeyA")]
    [DataRow("A", "KeyA")]
    [DataRow("!", "Digit1")]
    [DataRow("1", "Digit1")]
    [DataRow(" ", "Space")]
    [DataRow("/", "Slash")]
    [DataRow("+", "Equal")]
    [DataRow("é", null)]
    [DataRow("😀", null)]
    [DataRow("👩‍💻", null)]
    public void InputDispatchKeyEvent_CharacterKey_IsSentAsItself(string key, string? code)
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = JsonBuilder.FromObject(new { type = "keyDown", key, code, text = key });
        var action = translation.TranslateParams(cdpParams, "ctx-1")
            .GetProperty("actions")[0].GetProperty("actions")[0];

        Assert.AreEqual("keyDown", action.GetProperty("type").GetString());
        Assert.AreEqual(key, action.GetProperty("value").GetString());
    }

    [TestMethod]
    public void InputDispatchKeyEvent_EnterWithCarriageReturnText_IsEnter()
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = Parse("""{"type":"keyDown","key":"Enter","code":"Enter","text":"\r","unmodifiedText":"\r","modifiers":0}""");
        var action = translation.TranslateParams(cdpParams, "ctx-1")
            .GetProperty("actions")[0].GetProperty("actions")[0];

        Assert.AreEqual("keyDown", action.GetProperty("type").GetString());
        Assert.AreEqual("", action.GetProperty("value").GetString());
    }

    [TestMethod]
    public void InputDispatchKeyEvent_TextWithoutKey_IsSentAsTheText()
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = Parse("""{"type":"keyDown","text":"\r"}""");
        var action = translation.TranslateParams(cdpParams, "ctx-1")
            .GetProperty("actions")[0].GetProperty("actions")[0];

        Assert.AreEqual("", action.GetProperty("value").GetString());
    }

    /// <summary>
    /// WebDriver keeps a key source's held keys between commands and derives the modifiers from
    /// them, so the CDP bitmask adds nothing: the Shift that went down earlier is still held.
    /// </summary>
    [TestMethod]
    public void InputDispatchKeyEvent_Modifiers_SendOnlyTheKeyItself()
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = Parse("""{"type":"keyDown","key":"A","code":"KeyA","text":"A","modifiers":8}""");
        var seq = translation.TranslateParams(cdpParams, "ctx-1").GetProperty("actions")[0];

        Assert.AreEqual("keyboard-0", seq.GetProperty("id").GetString());
        Assert.AreEqual(1, seq.GetProperty("actions").GetArrayLength());
        Assert.AreEqual("A", seq.GetProperty("actions")[0].GetProperty("value").GetString());
    }

    [TestMethod]
    public void InputDispatchKeyEvent_ShortcutWithoutText_IsAKeyDown()
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = Parse("""{"type":"rawKeyDown","key":"a","code":"KeyA","modifiers":2}""");
        var action = translation.TranslateParams(cdpParams, "ctx-1")
            .GetProperty("actions")[0].GetProperty("actions")[0];

        Assert.AreEqual("keyDown", action.GetProperty("type").GetString());
        Assert.AreEqual("a", action.GetProperty("value").GetString());
    }

    /// <summary>
    /// A WebDriver key down already produces the key's character, so a CDP char event has nothing
    /// left to add. It becomes a pause rather than a second key down that would leave the key held.
    /// </summary>
    [TestMethod]
    public void InputDispatchKeyEvent_Char_IsAPause()
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = Parse("""{"type":"char","key":"a","text":"a"}""");
        var seq = translation.TranslateParams(cdpParams, "ctx-1").GetProperty("actions")[0];

        Assert.AreEqual("key", seq.GetProperty("type").GetString());
        var action = seq.GetProperty("actions")[0];
        Assert.AreEqual("pause", action.GetProperty("type").GetString());
        Assert.IsFalse(action.TryGetProperty("value", out _));
    }

    [TestMethod]
    [DataRow("CapsLock", "CapsLock")]
    [DataRow("NumLock", "NumLock")]
    [DataRow("ContextMenu", "ContextMenu")]
    [DataRow("AudioVolumeUp", "AudioVolumeUp")]
    public void InputDispatchKeyEvent_KeyWithoutWebDriverValue_Throws(string key, string code)
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        var cdpParams = JsonBuilder.FromObject(new { type = "rawKeyDown", key, code });

        var ex = Assert.ThrowsException<NotSupportedException>(
            () => translation.TranslateParams(cdpParams, "ctx-1"));
        StringAssert.Contains(ex.Message, $"'{key}'");
    }

    [TestMethod]
    public void InputDispatchKeyEvent_WithoutKeyOrText_Throws()
    {
        var translation = GetTranslation("Input.dispatchKeyEvent");

        Assert.ThrowsException<ArgumentException>(
            () => translation.TranslateParams(Parse("""{"type":"keyDown","code":"KeyA"}"""), "ctx-1"));
    }

    /// <summary>
    /// Every key the keyboard can press either reaches WebDriver as a single character or is one
    /// of the few keys WebDriver has no value for, which fail with a clear message.
    /// </summary>
    [TestMethod]
    public void InputDispatchKeyEvent_EveryLayoutKey_TranslatesToOneCharacter()
    {
        string[] noWebDriverValue =
        [
            "CapsLock", "NumLock", "ScrollLock", "PrintScreen", "ContextMenu",
            "AudioVolumeMute", "AudioVolumeDown", "AudioVolumeUp",
            "MediaTrackNext", "MediaTrackPrevious", "MediaStop", "MediaPlayPause",
        ];

        var translation = GetTranslation("Input.dispatchKeyEvent");
        var defs = KeyDefinitions.Keys.Values
            .SelectMany(d => d.Shifted is null ? new[] { d } : new[] { d, d.Shifted })
            .Distinct();

        foreach (var def in defs)
        {
            var cdpParams = JsonBuilder.FromObject(new { type = "keyUp", key = def.Key, code = def.Code });

            if (noWebDriverValue.Contains(def.Code))
            {
                Assert.ThrowsException<NotSupportedException>(
                    () => translation.TranslateParams(cdpParams, "ctx-1"), def.Code);
                continue;
            }

            var value = translation.TranslateParams(cdpParams, "ctx-1")
                .GetProperty("actions")[0].GetProperty("actions")[0].GetProperty("value").GetString()!;

            Assert.AreEqual(1, new System.Globalization.StringInfo(value).LengthInTextElements,
                $"{def.Key} ({def.Code}) became '{value}'");
            if (def.Key.Length > 1)
                Assert.IsTrue(value[0] is >= '' and <= '', $"{def.Key} ({def.Code}) became '{value}'");
        }
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
