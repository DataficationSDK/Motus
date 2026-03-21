using System.Text.Json;

namespace Motus;

/// <summary>
/// Defines a bidirectional translation between a CDP command and its BiDi equivalent.
/// Each translation converts CDP params to BiDi params and BiDi results back to CDP results.
/// </summary>
internal interface IBiDiTranslation
{
    string CdpMethod { get; }
    string BiDiMethod { get; }
    JsonElement TranslateParams(JsonElement cdpParams, string? contextId);
    JsonElement TranslateResult(JsonElement bidiResult);
}

/// <summary>
/// Static registry mapping CDP method names to BiDi translation handlers.
/// </summary>
internal static class BiDiTranslationRegistry
{
    private static readonly Dictionary<string, IBiDiTranslation> s_translations = BuildTranslations();

    internal static bool TryGet(string cdpMethod, out IBiDiTranslation? translation)
        => s_translations.TryGetValue(cdpMethod, out translation);

    private static Dictionary<string, IBiDiTranslation> BuildTranslations()
    {
        IBiDiTranslation[] all =
        [
            // Session / Browser
            new BrowserGetVersionTranslation(),
            new BrowserCloseTranslation(),

            // Target management
            new TargetCreateTargetTranslation(),
            new TargetCloseTargetTranslation(),
            new TargetAttachToTargetTranslation(),
            new TargetSetAutoAttachTranslation(),
            new TargetCreateBrowserContextTranslation(),
            new TargetDisposeBrowserContextTranslation(),

            // Page / Navigation
            new PageNavigateTranslation(),
            new PageReloadTranslation(),

            // Script
            new RuntimeEvaluateTranslation(),
            new RuntimeCallFunctionOnTranslation(),
            new RuntimeReleaseObjectTranslation(),

            // Input
            new InputDispatchKeyEventTranslation(),
            new InputDispatchMouseEventTranslation(),
            new InputDispatchTouchEventTranslation(),

            // Network
            new FetchEnableTranslation(),
            new FetchContinueRequestTranslation(),
            new FetchFulfillRequestTranslation(),
            new FetchFailRequestTranslation(),

            // Domain enables (no-ops in BiDi)
            new NoOpTranslation("Page.enable", "session.status"),
            new NoOpTranslation("DOM.enable", "session.status"),
            new NoOpTranslation("CSS.enable", "session.status"),
            new NoOpTranslation("Log.enable", "session.status"),
            new NoOpTranslation("Network.enable", "session.status"),
            new NoOpTranslation("Runtime.enable", "session.status"),
            new NoOpTranslation("Page.setLifecycleEventsEnabled", "session.status"),
            new NoOpTranslation("Fetch.disable", "session.status"),
        ];

        return all.ToDictionary(t => t.CdpMethod);
    }
}

// ──────────────────────────────────────────────
// Helper: JSON builder utilities
// ──────────────────────────────────────────────

internal static class JsonBuilder
{
    private static readonly JsonElement s_emptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    internal static JsonElement Empty() => s_emptyObject;

    internal static JsonElement FromObject(object obj)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(obj);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    internal static string? GetStringOrNull(this JsonElement el, string property)
        => el.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    internal static bool GetBoolOrDefault(this JsonElement el, string property, bool defaultValue = false)
        => el.TryGetProperty(property, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? p.GetBoolean()
            : defaultValue;

    internal static double? GetDoubleOrNull(this JsonElement el, string property)
        => el.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetDouble()
            : null;

    internal static int GetIntOrDefault(this JsonElement el, string property, int defaultValue = 0)
        => el.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetInt32()
            : defaultValue;
}

// ──────────────────────────────────────────────
// No-op translation (domain enables, etc.)
// ──────────────────────────────────────────────

internal sealed class NoOpTranslation(string cdpMethod, string bidiMethod) : IBiDiTranslation
{
    public string CdpMethod => cdpMethod;
    public string BiDiMethod => bidiMethod;

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.Empty();

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

// ──────────────────────────────────────────────
// Session / Browser translations
// ──────────────────────────────────────────────

/// <summary>
/// Browser.getVersion -> session.status
/// CDP result: { protocolVersion, product, revision, userAgent, jsVersion }
/// BiDi result: { ready, message }
/// </summary>
internal sealed class BrowserGetVersionTranslation : IBiDiTranslation
{
    public string CdpMethod => "Browser.getVersion";
    public string BiDiMethod => "session.status";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.Empty();

    public JsonElement TranslateResult(JsonElement bidiResult)
    {
        var message = bidiResult.GetStringOrNull("message") ?? "BiDi";
        return JsonBuilder.FromObject(new
        {
            protocolVersion = "1.3",
            product = message,
            revision = string.Empty,
            userAgent = message,
            jsVersion = string.Empty
        });
    }
}

/// <summary>
/// Browser.close -> session.end
/// </summary>
internal sealed class BrowserCloseTranslation : IBiDiTranslation
{
    public string CdpMethod => "Browser.close";
    public string BiDiMethod => "session.end";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.Empty();

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

// ──────────────────────────────────────────────
// Target translations
// ──────────────────────────────────────────────

/// <summary>
/// Target.createTarget -> browsingContext.create
/// CDP params: { url, browserContextId?, ... }
/// CDP result: { targetId }
/// BiDi params: { type: "tab" }
/// BiDi result: { context }
/// </summary>
internal sealed class TargetCreateTargetTranslation : IBiDiTranslation
{
    public string CdpMethod => "Target.createTarget";
    public string BiDiMethod => "browsingContext.create";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.FromObject(new { type = "tab" });

    public JsonElement TranslateResult(JsonElement bidiResult)
    {
        var context = bidiResult.GetStringOrNull("context") ?? string.Empty;
        return JsonBuilder.FromObject(new { targetId = context });
    }
}

/// <summary>
/// Target.closeTarget -> browsingContext.close
/// CDP params: { targetId }
/// BiDi params: { context }
/// </summary>
internal sealed class TargetCloseTargetTranslation : IBiDiTranslation
{
    public string CdpMethod => "Target.closeTarget";
    public string BiDiMethod => "browsingContext.close";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var targetId = cdpParams.GetStringOrNull("targetId") ?? contextId ?? string.Empty;
        return JsonBuilder.FromObject(new { context = targetId });
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

/// <summary>
/// Target.attachToTarget -> no-op (BiDi has no attach concept)
/// Returns { sessionId: targetId } to satisfy the engine's expectation.
/// </summary>
internal sealed class TargetAttachToTargetTranslation : IBiDiTranslation
{
    public string CdpMethod => "Target.attachToTarget";
    public string BiDiMethod => "session.status";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.Empty();

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.FromObject(new { sessionId = "bidi-session" });
}

/// <summary>
/// Target.setAutoAttach -> no-op
/// </summary>
internal sealed class TargetSetAutoAttachTranslation : IBiDiTranslation
{
    public string CdpMethod => "Target.setAutoAttach";
    public string BiDiMethod => "session.status";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.Empty();

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

/// <summary>
/// Target.createBrowserContext -> no-op (BiDi handles contexts differently)
/// Returns a synthetic browser context ID.
/// </summary>
internal sealed class TargetCreateBrowserContextTranslation : IBiDiTranslation
{
    public string CdpMethod => "Target.createBrowserContext";
    public string BiDiMethod => "session.status";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.Empty();

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.FromObject(new { browserContextId = $"bidi-ctx-{Guid.NewGuid():N}" });
}

/// <summary>
/// Target.disposeBrowserContext -> no-op
/// </summary>
internal sealed class TargetDisposeBrowserContextTranslation : IBiDiTranslation
{
    public string CdpMethod => "Target.disposeBrowserContext";
    public string BiDiMethod => "session.status";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.Empty();

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

// ──────────────────────────────────────────────
// Page / Navigation translations
// ──────────────────────────────────────────────

/// <summary>
/// Page.navigate -> browsingContext.navigate
/// CDP params: { url, referrer?, frameId?, transitionType? }
/// CDP result: { frameId, loaderId?, errorText? }
/// BiDi params: { context, url, wait: "complete" }
/// BiDi result: { navigation, url }
/// </summary>
internal sealed class PageNavigateTranslation : IBiDiTranslation
{
    public string CdpMethod => "Page.navigate";
    public string BiDiMethod => "browsingContext.navigate";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var url = cdpParams.GetStringOrNull("url") ?? "about:blank";
        return JsonBuilder.FromObject(new
        {
            context = contextId ?? string.Empty,
            url,
            wait = "complete"
        });
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
    {
        var navigation = bidiResult.GetStringOrNull("navigation") ?? string.Empty;
        var url = bidiResult.GetStringOrNull("url") ?? string.Empty;
        return JsonBuilder.FromObject(new
        {
            frameId = url,
            loaderId = navigation
        });
    }
}

/// <summary>
/// Page.reload -> browsingContext.reload
/// </summary>
internal sealed class PageReloadTranslation : IBiDiTranslation
{
    public string CdpMethod => "Page.reload";
    public string BiDiMethod => "browsingContext.reload";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.FromObject(new
        {
            context = contextId ?? string.Empty,
            wait = "complete"
        });

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

// ──────────────────────────────────────────────
// Script translations
// ──────────────────────────────────────────────

/// <summary>
/// Runtime.evaluate -> script.evaluate
/// CDP params: { expression, returnByValue?, awaitPromise?, contextId? }
/// CDP result: { result: RuntimeRemoteObject, exceptionDetails? }
/// </summary>
internal sealed class RuntimeEvaluateTranslation : IBiDiTranslation
{
    public string CdpMethod => "Runtime.evaluate";
    public string BiDiMethod => "script.evaluate";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var expression = cdpParams.GetStringOrNull("expression") ?? string.Empty;
        var awaitPromise = cdpParams.GetBoolOrDefault("awaitPromise", false);
        var returnByValue = cdpParams.GetBoolOrDefault("returnByValue", false);

        return JsonBuilder.FromObject(new
        {
            expression,
            target = new { context = contextId ?? string.Empty },
            awaitPromise,
            resultOwnership = returnByValue ? (string?)null : "root"
        });
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => ScriptResultTranslator.ToCdpResult(bidiResult);
}

/// <summary>
/// Runtime.callFunctionOn -> script.callFunction
/// CDP params: { functionDeclaration, objectId?, arguments?, returnByValue?, awaitPromise? }
/// CDP result: { result: RuntimeRemoteObject, exceptionDetails? }
/// </summary>
internal sealed class RuntimeCallFunctionOnTranslation : IBiDiTranslation
{
    public string CdpMethod => "Runtime.callFunctionOn";
    public string BiDiMethod => "script.callFunction";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var functionDeclaration = cdpParams.GetStringOrNull("functionDeclaration") ?? string.Empty;
        var awaitPromise = cdpParams.GetBoolOrDefault("awaitPromise", false);
        var returnByValue = cdpParams.GetBoolOrDefault("returnByValue", false);

        // Build BiDi arguments from CDP arguments
        BiDiScriptLocalValue[]? bidiArgs = null;
        if (cdpParams.TryGetProperty("arguments", out var args) &&
            args.ValueKind == JsonValueKind.Array)
        {
            var list = new List<BiDiScriptLocalValue>();
            foreach (var arg in args.EnumerateArray())
            {
                if (arg.TryGetProperty("value", out var val))
                {
                    list.Add(new BiDiScriptLocalValue(
                        Type: MapValueKindToType(val.ValueKind),
                        Value: val));
                }
            }
            bidiArgs = list.Count > 0 ? list.ToArray() : null;
        }

        // Map objectId to a "this" reference
        BiDiScriptLocalValue? thisRef = null;
        var objectId = cdpParams.GetStringOrNull("objectId");
        if (objectId is not null)
        {
            thisRef = new BiDiScriptLocalValue(
                Type: "handle",
                Value: JsonSerializer.SerializeToElement(objectId));
        }

        return JsonSerializer.SerializeToElement(
            new BiDiScriptCallFunctionParams(
                FunctionDeclaration: functionDeclaration,
                AwaitPromise: awaitPromise,
                Target: new BiDiScriptTarget(Context: contextId ?? string.Empty),
                Arguments: bidiArgs,
                ResultOwnership: returnByValue ? null : "root",
                This: thisRef),
            BiDiJsonContext.Default.BiDiScriptCallFunctionParams);
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => ScriptResultTranslator.ToCdpResult(bidiResult);

    private static string MapValueKindToType(JsonValueKind kind) => kind switch
    {
        JsonValueKind.String => "string",
        JsonValueKind.Number => "number",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Null => "null",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        _ => "undefined"
    };
}

/// <summary>
/// Runtime.releaseObject -> no-op (BiDi handles object lifecycle differently)
/// </summary>
internal sealed class RuntimeReleaseObjectTranslation : IBiDiTranslation
{
    public string CdpMethod => "Runtime.releaseObject";
    public string BiDiMethod => "session.status";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
        => JsonBuilder.Empty();

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

/// <summary>
/// Shared utility for translating BiDi script results to CDP RuntimeRemoteObject shape.
/// </summary>
internal static class ScriptResultTranslator
{
    internal static JsonElement ToCdpResult(JsonElement bidiResult)
    {
        // BiDi result: { type: "success"|"exception", result?: { type, value?, handle? }, exceptionDetails? }
        var resultType = bidiResult.GetStringOrNull("type") ?? "success";

        if (resultType == "exception")
        {
            string errorText = "Evaluation failed";
            if (bidiResult.TryGetProperty("exceptionDetails", out var details) &&
                details.TryGetProperty("text", out var text))
            {
                errorText = text.GetString() ?? errorText;
            }

            return JsonBuilder.FromObject(new
            {
                result = new { type = "undefined" },
                exceptionDetails = new { text = errorText }
            });
        }

        // Success path
        if (bidiResult.TryGetProperty("result", out var remoteValue))
        {
            var type = remoteValue.GetStringOrNull("type") ?? "undefined";
            var handle = remoteValue.GetStringOrNull("handle");

            // Map BiDi type to CDP type
            var (cdpType, cdpSubtype) = MapBiDiTypeToCdp(type);

            if (remoteValue.TryGetProperty("value", out var value))
            {
                return JsonBuilder.FromObject(new
                {
                    result = new
                    {
                        type = cdpType,
                        subtype = cdpSubtype,
                        value = (object?)value,
                        objectId = handle
                    }
                });
            }

            return JsonBuilder.FromObject(new
            {
                result = new
                {
                    type = cdpType,
                    subtype = cdpSubtype,
                    objectId = handle
                }
            });
        }

        return JsonBuilder.FromObject(new { result = new { type = "undefined" } });
    }

    private static (string type, string? subtype) MapBiDiTypeToCdp(string biDiType) => biDiType switch
    {
        "string" => ("string", null),
        "number" => ("number", null),
        "boolean" => ("boolean", null),
        "null" => ("object", "null"),
        "undefined" => ("undefined", null),
        "array" => ("object", "array"),
        "node" => ("object", "node"),
        "regexp" => ("object", "regexp"),
        "date" => ("object", "date"),
        "map" => ("object", "map"),
        "set" => ("object", "set"),
        "object" => ("object", null),
        _ => ("object", null)
    };
}

// ──────────────────────────────────────────────
// Input translations
// ──────────────────────────────────────────────

/// <summary>
/// Input.dispatchKeyEvent -> input.performActions (key)
/// CDP params: { type: "rawKeyDown"|"char"|"keyUp", key, code, ... }
/// BiDi: single-action key sequence
/// </summary>
internal sealed class InputDispatchKeyEventTranslation : IBiDiTranslation
{
    public string CdpMethod => "Input.dispatchKeyEvent";
    public string BiDiMethod => "input.performActions";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var cdpType = cdpParams.GetStringOrNull("type") ?? "keyDown";
        var key = cdpParams.GetStringOrNull("key") ?? cdpParams.GetStringOrNull("text") ?? string.Empty;

        var actionType = cdpType switch
        {
            "rawKeyDown" or "keyDown" => "keyDown",
            "keyUp" => "keyUp",
            _ => "keyDown" // "char" maps to keyDown in BiDi
        };

        return JsonSerializer.SerializeToElement(
            new BiDiInputPerformActionsParams(
                Context: contextId ?? string.Empty,
                Actions:
                [
                    new BiDiInputActionSequence(
                        Type: "key",
                        Id: "keyboard-0",
                        Actions: [new BiDiInputAction(Type: actionType, Value: key)])
                ]),
            BiDiJsonContext.Default.BiDiInputPerformActionsParams);
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

/// <summary>
/// Input.dispatchMouseEvent -> input.performActions (pointer)
/// CDP params: { type: "mousePressed"|"mouseReleased"|"mouseMoved", x, y, button, clickCount, ... }
/// BiDi: single-action pointer sequence
/// </summary>
internal sealed class InputDispatchMouseEventTranslation : IBiDiTranslation
{
    public string CdpMethod => "Input.dispatchMouseEvent";
    public string BiDiMethod => "input.performActions";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var cdpType = cdpParams.GetStringOrNull("type") ?? "mouseMoved";
        var x = cdpParams.GetDoubleOrNull("x") ?? 0;
        var y = cdpParams.GetDoubleOrNull("y") ?? 0;
        var button = MapButton(cdpParams.GetStringOrNull("button"));

        var actionType = cdpType switch
        {
            "mousePressed" => "pointerDown",
            "mouseReleased" => "pointerUp",
            "mouseMoved" => "pointerMove",
            "mouseWheel" => "scroll",
            _ => "pointerMove"
        };

        BiDiInputAction action;
        if (actionType is "pointerDown" or "pointerUp")
        {
            action = new BiDiInputAction(Type: actionType, Button: button);
        }
        else
        {
            action = new BiDiInputAction(Type: actionType, X: x, Y: y, Origin: "viewport");
        }

        // For click: need pointerMove first, then the action
        BiDiInputAction[] actions;
        if (actionType is "pointerDown" or "pointerUp")
        {
            actions = [new BiDiInputAction(Type: "pointerMove", X: x, Y: y, Origin: "viewport"), action];
        }
        else
        {
            actions = [action];
        }

        return JsonSerializer.SerializeToElement(
            new BiDiInputPerformActionsParams(
                Context: contextId ?? string.Empty,
                Actions:
                [
                    new BiDiInputActionSequence(
                        Type: "pointer",
                        Id: "mouse-0",
                        Actions: actions,
                        Parameters: new BiDiInputSourceParameters(PointerType: "mouse"))
                ]),
            BiDiJsonContext.Default.BiDiInputPerformActionsParams);
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();

    private static int MapButton(string? cdpButton) => cdpButton switch
    {
        "left" => 0,
        "middle" => 1,
        "right" => 2,
        _ => 0
    };
}

/// <summary>
/// Input.dispatchTouchEvent -> input.performActions (pointer with touch type)
/// </summary>
internal sealed class InputDispatchTouchEventTranslation : IBiDiTranslation
{
    public string CdpMethod => "Input.dispatchTouchEvent";
    public string BiDiMethod => "input.performActions";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var cdpType = cdpParams.GetStringOrNull("type") ?? "touchStart";
        double x = 0, y = 0;

        // Touch events carry coordinates in a touchPoints array
        if (cdpParams.TryGetProperty("touchPoints", out var points) &&
            points.ValueKind == JsonValueKind.Array)
        {
            foreach (var point in points.EnumerateArray())
            {
                x = point.GetDoubleOrNull("x") ?? 0;
                y = point.GetDoubleOrNull("y") ?? 0;
                break;
            }
        }

        var actionType = cdpType switch
        {
            "touchStart" => "pointerDown",
            "touchEnd" => "pointerUp",
            "touchMove" => "pointerMove",
            _ => "pointerMove"
        };

        BiDiInputAction[] actions;
        if (actionType is "pointerDown" or "pointerUp")
        {
            actions =
            [
                new BiDiInputAction(Type: "pointerMove", X: x, Y: y, Origin: "viewport"),
                new BiDiInputAction(Type: actionType, Button: 0)
            ];
        }
        else
        {
            actions = [new BiDiInputAction(Type: "pointerMove", X: x, Y: y, Origin: "viewport")];
        }

        return JsonSerializer.SerializeToElement(
            new BiDiInputPerformActionsParams(
                Context: contextId ?? string.Empty,
                Actions:
                [
                    new BiDiInputActionSequence(
                        Type: "pointer",
                        Id: "touch-0",
                        Actions: actions,
                        Parameters: new BiDiInputSourceParameters(PointerType: "touch"))
                ]),
            BiDiJsonContext.Default.BiDiInputPerformActionsParams);
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

// ──────────────────────────────────────────────
// Network translations
// ──────────────────────────────────────────────

/// <summary>
/// Fetch.enable -> network.addIntercept
/// CDP params: { patterns?, handleAuthRequests? }
/// BiDi params: { phases: ["beforeRequestSent"], urlPatterns? }
/// </summary>
internal sealed class FetchEnableTranslation : IBiDiTranslation
{
    public string CdpMethod => "Fetch.enable";
    public string BiDiMethod => "network.addIntercept";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        // Map CDP patterns to BiDi URL patterns
        BiDiNetworkUrlPattern[]? urlPatterns = null;
        if (cdpParams.TryGetProperty("patterns", out var patterns) &&
            patterns.ValueKind == JsonValueKind.Array)
        {
            var list = new List<BiDiNetworkUrlPattern>();
            foreach (var pattern in patterns.EnumerateArray())
            {
                var urlPattern = pattern.GetStringOrNull("urlPattern");
                if (urlPattern is not null)
                    list.Add(new BiDiNetworkUrlPattern(Type: "string", Pattern: urlPattern));
            }
            if (list.Count > 0) urlPatterns = list.ToArray();
        }

        return JsonSerializer.SerializeToElement(
            new BiDiNetworkAddInterceptParams(
                Phases: ["beforeRequestSent"],
                UrlPatterns: urlPatterns),
            BiDiJsonContext.Default.BiDiNetworkAddInterceptParams);
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
    {
        // The intercept ID needs to be stored by the session for later removal.
        // Return it in a shape the session can read, but the CDP engine expects empty.
        return JsonBuilder.Empty();
    }
}

/// <summary>
/// Fetch.continueRequest -> network.continueRequest
/// CDP params: { requestId, url?, method?, postData?, headers? }
/// BiDi params: { request, url?, method?, body?, headers? }
/// </summary>
internal sealed class FetchContinueRequestTranslation : IBiDiTranslation
{
    public string CdpMethod => "Fetch.continueRequest";
    public string BiDiMethod => "network.continueRequest";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var requestId = cdpParams.GetStringOrNull("requestId") ?? string.Empty;
        var url = cdpParams.GetStringOrNull("url");
        var method = cdpParams.GetStringOrNull("method");

        // Map CDP headers to BiDi headers
        BiDiNetworkHeader[]? headers = null;
        if (cdpParams.TryGetProperty("headers", out var hdrs) &&
            hdrs.ValueKind == JsonValueKind.Array)
        {
            var list = new List<BiDiNetworkHeader>();
            foreach (var h in hdrs.EnumerateArray())
            {
                var name = h.GetStringOrNull("name");
                var value = h.GetStringOrNull("value");
                if (name is not null)
                    list.Add(new BiDiNetworkHeader(name, new BiDiNetworkBytesValue("string", value)));
            }
            if (list.Count > 0) headers = list.ToArray();
        }

        // Map postData to body
        BiDiNetworkBytesValue? body = null;
        var postData = cdpParams.GetStringOrNull("postData");
        if (postData is not null)
            body = new BiDiNetworkBytesValue("string", postData);

        return JsonSerializer.SerializeToElement(
            new BiDiNetworkContinueRequestParams(
                Request: requestId,
                Url: url,
                Method: method,
                Body: body,
                Headers: headers),
            BiDiJsonContext.Default.BiDiNetworkContinueRequestParams);
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

/// <summary>
/// Fetch.fulfillRequest -> network.provideResponse
/// CDP params: { requestId, responseCode, responseHeaders?, body?, responsePhrase? }
/// BiDi params: { request, statusCode?, reasonPhrase?, headers?, body? }
/// </summary>
internal sealed class FetchFulfillRequestTranslation : IBiDiTranslation
{
    public string CdpMethod => "Fetch.fulfillRequest";
    public string BiDiMethod => "network.provideResponse";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var requestId = cdpParams.GetStringOrNull("requestId") ?? string.Empty;
        var statusCode = cdpParams.GetIntOrDefault("responseCode", 200);
        var reasonPhrase = cdpParams.GetStringOrNull("responsePhrase");

        // Map headers
        BiDiNetworkHeader[]? headers = null;
        if (cdpParams.TryGetProperty("responseHeaders", out var hdrs) &&
            hdrs.ValueKind == JsonValueKind.Array)
        {
            var list = new List<BiDiNetworkHeader>();
            foreach (var h in hdrs.EnumerateArray())
            {
                var name = h.GetStringOrNull("name");
                var value = h.GetStringOrNull("value");
                if (name is not null)
                    list.Add(new BiDiNetworkHeader(name, new BiDiNetworkBytesValue("string", value)));
            }
            if (list.Count > 0) headers = list.ToArray();
        }

        // Map body
        BiDiNetworkBytesValue? body = null;
        var bodyStr = cdpParams.GetStringOrNull("body");
        if (bodyStr is not null)
            body = new BiDiNetworkBytesValue("base64", bodyStr);

        return JsonSerializer.SerializeToElement(
            new BiDiNetworkProvideResponseParams(
                Request: requestId,
                StatusCode: statusCode,
                ReasonPhrase: reasonPhrase,
                Headers: headers,
                Body: body),
            BiDiJsonContext.Default.BiDiNetworkProvideResponseParams);
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}

/// <summary>
/// Fetch.failRequest -> network.failRequest
/// CDP params: { requestId, errorReason }
/// BiDi params: { request }
/// </summary>
internal sealed class FetchFailRequestTranslation : IBiDiTranslation
{
    public string CdpMethod => "Fetch.failRequest";
    public string BiDiMethod => "network.failRequest";

    public JsonElement TranslateParams(JsonElement cdpParams, string? contextId)
    {
        var requestId = cdpParams.GetStringOrNull("requestId") ?? string.Empty;
        return JsonSerializer.SerializeToElement(
            new BiDiNetworkFailRequestParams(requestId),
            BiDiJsonContext.Default.BiDiNetworkFailRequestParams);
    }

    public JsonElement TranslateResult(JsonElement bidiResult)
        => JsonBuilder.Empty();
}
