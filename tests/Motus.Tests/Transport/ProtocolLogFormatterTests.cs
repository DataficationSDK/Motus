using System.Text.Json;

namespace Motus.Tests.Transport;

[TestClass]
public class ProtocolLogFormatterTests
{
    private static string Format(string json, string? method = null, int maxValueLength = 1024, bool redact = true)
    {
        using var doc = JsonDocument.Parse(json);
        return ProtocolLogFormatter.Format(doc.RootElement, method, maxValueLength, redact);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [TestMethod]
    public void Trims_LongStringValues_KeepsValidJson()
    {
        var data = new string('A', 200_000);
        var formatted = Format($$"""{"data":"{{data}}"}""");

        var value = Parse(formatted).GetProperty("data").GetString()!;
        Assert.IsTrue(value.StartsWith(new string('A', 1024) + "…[+198976 chars]", StringComparison.Ordinal));
        Assert.AreEqual(1024 + "…[+198976 chars]".Length, value.Length);
    }

    [TestMethod]
    public void Respects_MaxValueLength_Option()
    {
        var formatted = Format("""{"expression":"abcdefghij"}""", maxValueLength: 4);

        Assert.AreEqual("abcd…[+6 chars]", Parse(formatted).GetProperty("expression").GetString());
    }

    [TestMethod]
    public void MaxValueLength_ZeroOrLess_DoesNotTrim()
    {
        var formatted = Format("""{"expression":"abcdefghij"}""", maxValueLength: 0);

        Assert.AreEqual("abcdefghij", Parse(formatted).GetProperty("expression").GetString());
    }

    [TestMethod]
    public void Trim_DoesNotSplitSurrogatePair()
    {
        var trimmed = ProtocolLogFormatter.Trim("ab\U0001F600cd", 3);

        Assert.AreEqual("ab…[+4 chars]", trimmed);
    }

    [TestMethod]
    public void CapsLine_At64KB_AppendsByteCountMarker()
    {
        var nodes = string.Join(",", Enumerable.Range(0, 20_000).Select(i => $$"""{"nodeId":{{i}},"name":"DIV"}"""));
        var formatted = Format($$"""{"nodes":[{{nodes}}]}""");

        var marker = formatted.LastIndexOf("…[+", StringComparison.Ordinal);
        Assert.IsTrue(marker > 0, "the cut payload ends with a count of what was cut");
        Assert.IsTrue(formatted.EndsWith(" bytes]", StringComparison.Ordinal));
        Assert.AreEqual(ProtocolLogFormatter.MaxPayloadBytes, System.Text.Encoding.UTF8.GetByteCount(formatted[..marker]));
    }

    [TestMethod]
    public void Redacts_AuthorizationAndCookieHeaders_ObjectForm()
    {
        // The shape Chrome pushes unasked once Network is enabled.
        var formatted = Format("""
            {"requestId":"1","associatedCookies":[],"headers":{
              "Accept":"text/html","Authorization":"Bearer SECRET_TOKEN","Cookie":"sess=COOKIE_SECRET",
              "User-Agent":"Chrome","X-Api-Key":"k","x-auth-token":"t"}}
            """, "Network.requestWillBeSentExtraInfo");

        var headers = Parse(formatted).GetProperty("headers");
        Assert.AreEqual("<redacted>", headers.GetProperty("Authorization").GetString());
        Assert.AreEqual("<redacted>", headers.GetProperty("Cookie").GetString());
        Assert.AreEqual("<redacted>", headers.GetProperty("X-Api-Key").GetString());
        Assert.AreEqual("<redacted>", headers.GetProperty("x-auth-token").GetString());
        Assert.AreEqual("text/html", headers.GetProperty("Accept").GetString());
        Assert.AreEqual("Chrome", headers.GetProperty("User-Agent").GetString());
        Assert.IsFalse(formatted.Contains("SECRET", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Redacts_ExtraHttpHeaders_OnSend()
    {
        var formatted = Format("""{"headers":{"Authorization":"Bearer SECRET_TOKEN","X-Trace":"1"}}""",
            "Network.setExtraHTTPHeaders");

        Assert.IsFalse(formatted.Contains("SECRET_TOKEN", StringComparison.Ordinal));
        Assert.IsTrue(formatted.Contains("\"X-Trace\":\"1\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Redacts_HeaderArrayForm_FetchContinueRequest()
    {
        var formatted = Format("""
            {"requestId":"r","headers":[{"name":"Authorization","value":"Bearer SECRET"},{"name":"Accept","value":"*/*"}]}
            """, "Fetch.continueRequest");

        var headers = Parse(formatted).GetProperty("headers");
        Assert.AreEqual("Authorization", headers[0].GetProperty("name").GetString());
        Assert.AreEqual("<redacted>", headers[0].GetProperty("value").GetString());
        Assert.AreEqual("*/*", headers[1].GetProperty("value").GetString());
    }

    [TestMethod]
    public void Redacts_HeaderArrayForm_BiDiValueObject()
    {
        var formatted = Format("""
            {"request":{"headers":[
              {"name":"cookie","value":{"type":"string","value":"sess=SECRET"}},
              {"name":"accept","value":{"type":"string","value":"text/html"}}]}}
            """, "network.beforeRequestSent");

        var headers = Parse(formatted).GetProperty("request").GetProperty("headers");
        Assert.AreEqual("<redacted>", headers[0].GetProperty("value").GetString());
        Assert.AreEqual("text/html", headers[1].GetProperty("value").GetProperty("value").GetString());
    }

    [TestMethod]
    public void Redacts_HeadersText()
    {
        var formatted = Format("""
            {"headers":{"Content-Type":"text/html"},"headersText":"HTTP/1.1 200 OK\r\nSet-Cookie: srv=servervalue\r\n"}
            """, "Network.responseReceivedExtraInfo");

        Assert.AreEqual("<redacted>", Parse(formatted).GetProperty("headersText").GetString());
        Assert.IsFalse(formatted.Contains("servervalue", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Redacts_SetCookieHeader()
    {
        var formatted = Format("""{"headers":{"Set-Cookie":"srv=servervalue"}}""");

        Assert.IsFalse(formatted.Contains("servervalue", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Redacts_AssociatedCookieValues_KeepsNames()
    {
        var formatted = Format("""
            {"associatedCookies":[{"blockedReasons":[],"cookie":{"name":"sess","value":"COOKIE_SECRET","domain":"localhost"}}]}
            """, "Network.requestWillBeSentExtraInfo");

        var cookie = Parse(formatted).GetProperty("associatedCookies")[0].GetProperty("cookie");
        Assert.AreEqual("sess", cookie.GetProperty("name").GetString());
        Assert.AreEqual("<redacted>", cookie.GetProperty("value").GetString());
        Assert.AreEqual("localhost", cookie.GetProperty("domain").GetString());
    }

    [TestMethod]
    public void Redacts_NetworkSetCookie_RootValue()
    {
        var formatted = Format("""{"name":"sess","value":"COOKIE_SECRET","url":"http://localhost/"}""", "Network.setCookie");

        var root = Parse(formatted);
        Assert.AreEqual("sess", root.GetProperty("name").GetString());
        Assert.AreEqual("<redacted>", root.GetProperty("value").GetString());
        Assert.AreEqual("http://localhost/", root.GetProperty("url").GetString());
    }

    [TestMethod]
    public void RootValue_OfOtherMethods_IsKept()
    {
        var formatted = Format("""{"value":"typed"}""", "Runtime.callFunctionOn");

        Assert.AreEqual("typed", Parse(formatted).GetProperty("value").GetString());
    }

    [TestMethod]
    public void Redacts_GetCookiesResult_CookieValues()
    {
        var formatted = Format("""{"cookies":[{"name":"a","value":"1"},{"name":"b","value":"2"}]}""");

        var cookies = Parse(formatted).GetProperty("cookies");
        Assert.AreEqual("<redacted>", cookies[0].GetProperty("value").GetString());
        Assert.AreEqual("<redacted>", cookies[1].GetProperty("value").GetString());
        Assert.AreEqual("b", cookies[1].GetProperty("name").GetString());
    }

    [TestMethod]
    public void Redacts_BiDiCookieValueObject_Whole()
    {
        var formatted = Format("""{"cookie":{"name":"sess","value":{"type":"string","value":"SECRET"},"domain":"x"}}""",
            "storage.setCookie");

        Assert.AreEqual("<redacted>", Parse(formatted).GetProperty("cookie").GetProperty("value").GetString());
    }

    [TestMethod]
    public void Redacts_PasswordProperty_ContinueWithAuth()
    {
        var formatted = Format("""
            {"requestId":"r","authChallengeResponse":{"response":"ProvideCredentials","username":"admin","password":"hunter2"}}
            """, "Fetch.continueWithAuth");

        var auth = Parse(formatted).GetProperty("authChallengeResponse");
        Assert.AreEqual("admin", auth.GetProperty("username").GetString());
        Assert.AreEqual("<redacted>", auth.GetProperty("password").GetString());
    }

    [TestMethod]
    public void Redacts_PostData()
    {
        var formatted = Format("""
            {"request":{"url":"http://x/login","postData":"user=a&password=hunter2","postDataEntries":[{"bytes":"x"},{"bytes":"y"}]}}
            """, "Network.requestWillBeSent");

        var request = Parse(formatted).GetProperty("request");
        Assert.AreEqual("<redacted 23 chars>", request.GetProperty("postData").GetString());
        Assert.AreEqual("<redacted 2 entries>", request.GetProperty("postDataEntries").GetString());
        Assert.AreEqual("http://x/login", request.GetProperty("url").GetString());
    }

    [TestMethod]
    public void Redacts_BiDiContinueRequestBody_FromRoutePostData()
    {
        // What a Firefox route's ContinueAsync(PostData = ...) is sent as.
        using var cdp = JsonDocument.Parse("""{"requestId":"r1","postData":"user=a&password=hunter2"}""");
        var bidi = new FetchContinueRequestTranslation().TranslateParams(cdp.RootElement, contextId: null);

        var formatted = ProtocolLogFormatter.Format(bidi, "network.continueRequest", 1024, redactSecrets: true);

        Assert.IsFalse(formatted.Contains("hunter2", StringComparison.Ordinal), formatted);
        var root = Parse(formatted);
        Assert.AreEqual("<redacted 23 chars>", root.GetProperty("body").GetString());
        Assert.AreEqual("r1", root.GetProperty("request").GetString());
    }

    [TestMethod]
    public void Keeps_BodyOfOtherMethods_AndBelowTheRoot()
    {
        var response = Format("""{"request":"r1","body":{"type":"base64","value":"PGh0bWw+"}}""", "network.provideResponse");
        Assert.AreEqual("PGh0bWw+", Parse(response).GetProperty("body").GetProperty("value").GetString());

        var nested = Format("""{"request":"r1","extra":{"body":"kept"}}""", "network.continueRequest");
        Assert.AreEqual("kept", Parse(nested).GetProperty("extra").GetProperty("body").GetString());
    }

    [TestMethod]
    public void RedactSecretsFalse_KeepsBiDiContinueRequestBody()
    {
        var formatted = Format("""{"request":"r1","body":{"type":"string","value":"a=b"}}""", "network.continueRequest",
            redact: false);

        Assert.AreEqual("a=b", Parse(formatted).GetProperty("body").GetProperty("value").GetString());
    }

    [TestMethod]
    public void DoesNotRedact_DispatchKeyEvent()
    {
        var formatted = Format("""{"type":"keyDown","key":"p","code":"KeyP","text":"p","unmodifiedText":"p"}""",
            "Input.dispatchKeyEvent");

        var root = Parse(formatted);
        Assert.AreEqual("p", root.GetProperty("key").GetString());
        Assert.AreEqual("p", root.GetProperty("text").GetString());
        Assert.AreEqual("KeyP", root.GetProperty("code").GetString());
    }

    [TestMethod]
    public void DoesNotRedact_ScriptArguments()
    {
        var formatted = Format("""{"functionDeclaration":"function(v){this.value=v}","arguments":[{"value":"hunter2"}]}""",
            "Runtime.callFunctionOn");

        Assert.AreEqual("hunter2", Parse(formatted).GetProperty("arguments")[0].GetProperty("value").GetString());
    }

    [TestMethod]
    public void RedactSecretsFalse_LeavesEverything()
    {
        var formatted = Format("""
            {"headers":{"Authorization":"Bearer SECRET"},"password":"hunter2","postData":"a=b","cookies":[{"name":"n","value":"v"}]}
            """, redact: false);

        Assert.IsTrue(formatted.Contains("Bearer SECRET", StringComparison.Ordinal));
        Assert.IsTrue(formatted.Contains("hunter2", StringComparison.Ordinal));
        Assert.IsTrue(formatted.Contains("\"postData\":\"a=b\"", StringComparison.Ordinal));
        Assert.IsTrue(formatted.Contains("\"value\":\"v\"", StringComparison.Ordinal));
        Assert.IsFalse(formatted.Contains("<redacted", StringComparison.Ordinal));
    }

    [TestMethod]
    public void NonAscii_IsNotEscaped()
    {
        var formatted = Format("""{"text":"héllo 日本 <b>"}""");

        Assert.AreEqual("""{"text":"héllo 日本 <b>"}""", formatted);
    }

    [TestMethod]
    public void NumbersBooleansAndNulls_AreKept()
    {
        var formatted = Format("""{"a":1.5,"b":true,"c":null,"d":[1,2]}""");

        Assert.AreEqual("""{"a":1.5,"b":true,"c":null,"d":[1,2]}""", formatted);
    }

    [TestMethod]
    public void FormatMalformed_KeepsOneLine()
    {
        var text = ProtocolLogFormatter.FormatMalformed("not\njson\r"u8, 1024);

        Assert.AreEqual("not\\njson\\r", text);
    }
}
