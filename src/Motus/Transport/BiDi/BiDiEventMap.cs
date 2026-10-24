using System.Text.Json;

namespace Motus;

/// <summary>
/// Defines a bidirectional translation between a CDP event and its BiDi equivalent.
/// </summary>
internal interface IBiDiEventTranslation
{
    string CdpEventName { get; }
    string BiDiEventName { get; }
    JsonElement TranslateEvent(JsonElement bidiParams);

    /// <summary>
    /// Whether the event has to be subscribed for every browsing context rather than for the
    /// session's own, because a browser does not deliver it to a subscription scoped to one.
    /// </summary>
    bool SubscribeToAllContexts => false;
}

/// <summary>
/// Static lookup from CDP event names to BiDi event names and their payload translations.
/// </summary>
/// <remarks>
/// Most CDP events have exactly one BiDi counterpart. A few are reported by BiDi as several events
/// where CDP has one, and those CDP names carry one translation per BiDi event, all of which a
/// subscriber to the CDP name receives.
/// </remarks>
internal static class BiDiEventMap
{
    private static readonly Dictionary<string, IBiDiEventTranslation[]> s_byCdpName = BuildMap();

    internal static string? ToBiDiEventName(string cdpEventName)
        => GetEventTranslation(cdpEventName)?.BiDiEventName;

    /// <summary>
    /// The first translation registered for a CDP event, or <see langword="null"/> when it has none.
    /// </summary>
    internal static IBiDiEventTranslation? GetEventTranslation(string cdpEventName)
        => s_byCdpName.TryGetValue(cdpEventName, out var t) ? t[0] : null;

    /// <summary>
    /// Every translation registered for a CDP event, one per BiDi event it is reported as.
    /// </summary>
    internal static IReadOnlyList<IBiDiEventTranslation> GetEventTranslations(string cdpEventName)
        => s_byCdpName.TryGetValue(cdpEventName, out var t) ? t : [];

    private static Dictionary<string, IBiDiEventTranslation[]> BuildMap()
    {
        IBiDiEventTranslation[] all =
        [
            new LoadEventFiredTranslation(),
            new DomContentLoadedTranslation(),
            new FrameNavigatedTranslation(),
            new FragmentNavigatedTranslation(),
            new HistoryUpdatedTranslation(),
            new DialogOpeningTranslation(),
            new DialogClosedTranslation(),
            new RequestPausedTranslation(),
            new RequestWillBeSentTranslation(),
            new ResponseReceivedTranslation(),
            new AttachedToTargetTranslation(),
            new DetachedFromTargetTranslation(),
            new ConsoleApiCalledTranslation(),
        ];

        return all
            .GroupBy(t => t.CdpEventName)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}

// ──────────────────────────────────────────────
// Page lifecycle events
// ──────────────────────────────────────────────

/// <summary>
/// Page.loadEventFired -> browsingContext.load
/// BiDi params: { context, navigation?, timestamp, url }
/// CDP params: { timestamp }
/// </summary>
internal sealed class LoadEventFiredTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Page.loadEventFired";
    public string BiDiEventName => "browsingContext.load";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var timestamp = bidiParams.GetDoubleOrNull("timestamp") ?? 0;
        return JsonBuilder.FromObject(new { timestamp });
    }
}

/// <summary>
/// Page.domContentEventFired -> browsingContext.domContentLoaded
/// </summary>
internal sealed class DomContentLoadedTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Page.domContentEventFired";
    public string BiDiEventName => "browsingContext.domContentLoaded";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var timestamp = bidiParams.GetDoubleOrNull("timestamp") ?? 0;
        return JsonBuilder.FromObject(new { timestamp });
    }
}

/// <summary>
/// Page.frameNavigated -> browsingContext.navigationStarted
/// BiDi params: { context, navigation, url, timestamp }
/// CDP params: { frame: { id, url, ... } }
/// </summary>
internal sealed class FrameNavigatedTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Page.frameNavigated";
    public string BiDiEventName => "browsingContext.navigationStarted";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var contextId = bidiParams.GetStringOrNull("context") ?? string.Empty;
        var url = bidiParams.GetStringOrNull("url") ?? string.Empty;

        return JsonBuilder.FromObject(new
        {
            frame = new
            {
                id = contextId,
                url,
                securityOrigin = string.Empty,
                mimeType = "text/html"
            }
        });
    }
}

/// <summary>
/// Page.navigatedWithinDocument -> browsingContext.fragmentNavigated
/// BiDi params: { context, navigation, timestamp, url }
/// CDP params: { frameId, url, navigationType }
/// </summary>
internal sealed class FragmentNavigatedTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Page.navigatedWithinDocument";
    public string BiDiEventName => "browsingContext.fragmentNavigated";

    // Firefox accepts a subscription to this event scoped to one browsing context and then never
    // delivers it, while a subscription with no contexts listed is delivered as expected.
    public bool SubscribeToAllContexts => true;

    public JsonElement TranslateEvent(JsonElement bidiParams) =>
        JsonBuilder.FromObject(new
        {
            frameId = bidiParams.GetStringOrNull("context") ?? string.Empty,
            url = bidiParams.GetStringOrNull("url") ?? string.Empty,
            navigationType = "fragment"
        });
}

/// <summary>
/// Page.navigatedWithinDocument -> browsingContext.historyUpdated
/// BiDi params: { context, timestamp, url }
/// CDP params: { frameId, url, navigationType }
/// </summary>
/// <remarks>
/// BiDi reports a <c>history.pushState</c> or <c>history.replaceState</c> separately from a
/// fragment navigation, where CDP reports both as the one event.
/// </remarks>
internal sealed class HistoryUpdatedTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Page.navigatedWithinDocument";
    public string BiDiEventName => "browsingContext.historyUpdated";

    public JsonElement TranslateEvent(JsonElement bidiParams) =>
        JsonBuilder.FromObject(new
        {
            frameId = bidiParams.GetStringOrNull("context") ?? string.Empty,
            url = bidiParams.GetStringOrNull("url") ?? string.Empty,
            navigationType = "historyApi"
        });
}

// ──────────────────────────────────────────────
// Dialog events
// ──────────────────────────────────────────────

/// <summary>
/// Page.javascriptDialogOpening -> browsingContext.userPromptOpened
/// BiDi params: { context, type, message, defaultValue? }
/// CDP params: { url, message, type, hasBrowserHandler, defaultPrompt? }
/// </summary>
internal sealed class DialogOpeningTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Page.javascriptDialogOpening";
    public string BiDiEventName => "browsingContext.userPromptOpened";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var message = bidiParams.GetStringOrNull("message") ?? string.Empty;
        var type = bidiParams.GetStringOrNull("type") ?? "alert";
        var defaultValue = bidiParams.GetStringOrNull("defaultValue");

        return JsonBuilder.FromObject(new
        {
            url = string.Empty,
            message,
            type,
            hasBrowserHandler = false,
            defaultPrompt = defaultValue
        });
    }
}

/// <summary>
/// Page.javascriptDialogClosed -> browsingContext.userPromptClosed
/// BiDi params: { context, accepted, userText? }
/// CDP params: { result, userInput }
/// </summary>
internal sealed class DialogClosedTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Page.javascriptDialogClosed";
    public string BiDiEventName => "browsingContext.userPromptClosed";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var accepted = bidiParams.GetBoolOrDefault("accepted", false);
        var userText = bidiParams.GetStringOrNull("userText");

        return JsonBuilder.FromObject(new
        {
            result = accepted,
            userInput = userText
        });
    }
}

// ──────────────────────────────────────────────
// Network events
// ──────────────────────────────────────────────

/// <summary>
/// Fetch.requestPaused -> network.beforeRequestSent
/// BiDi params: { context, navigation?, request: { request, url, method, headers, ... } }
/// CDP params: { requestId, request, frameId, resourceType, ... }
/// </summary>
internal sealed class RequestPausedTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Fetch.requestPaused";
    public string BiDiEventName => "network.beforeRequestSent";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var contextId = bidiParams.GetStringOrNull("context") ?? string.Empty;
        string requestId = string.Empty, url = string.Empty, method = "GET";

        if (bidiParams.TryGetProperty("request", out var req))
        {
            requestId = req.GetStringOrNull("request") ?? string.Empty;
            url = req.GetStringOrNull("url") ?? string.Empty;
            method = req.GetStringOrNull("method") ?? "GET";
        }

        return JsonBuilder.FromObject(new
        {
            requestId,
            request = new { url, method, headers = new Dictionary<string, string>() },
            frameId = contextId,
            resourceType = "Document",
            networkId = requestId
        });
    }
}

/// <summary>
/// Network.requestWillBeSent -> network.beforeRequestSent
/// </summary>
internal sealed class RequestWillBeSentTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Network.requestWillBeSent";
    public string BiDiEventName => "network.beforeRequestSent";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var contextId = bidiParams.GetStringOrNull("context") ?? string.Empty;
        string requestId = string.Empty, url = string.Empty, method = "GET";

        if (bidiParams.TryGetProperty("request", out var req))
        {
            requestId = req.GetStringOrNull("request") ?? string.Empty;
            url = req.GetStringOrNull("url") ?? string.Empty;
            method = req.GetStringOrNull("method") ?? "GET";
        }

        return JsonBuilder.FromObject(new
        {
            requestId,
            request = new { url, method, headers = new Dictionary<string, string>() },
            frameId = contextId,
            type = "Document"
        });
    }
}

/// <summary>
/// Network.responseReceived -> network.responseCompleted
/// BiDi params: { context, request, response: { url, protocol, status, statusText, headers, ... } }
/// CDP params: { requestId, response: { url, status, statusText, headers, ... }, frameId, type }
/// </summary>
internal sealed class ResponseReceivedTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Network.responseReceived";
    public string BiDiEventName => "network.responseCompleted";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var contextId = bidiParams.GetStringOrNull("context") ?? string.Empty;
        string requestId = string.Empty;
        string url = string.Empty;
        int status = 0;
        string statusText = string.Empty;

        if (bidiParams.TryGetProperty("request", out var reqObj) &&
            reqObj.ValueKind == JsonValueKind.String)
        {
            requestId = reqObj.GetString() ?? string.Empty;
        }

        if (bidiParams.TryGetProperty("response", out var resp))
        {
            url = resp.GetStringOrNull("url") ?? string.Empty;
            status = resp.GetIntOrDefault("status", 0);
            statusText = resp.GetStringOrNull("statusText") ?? string.Empty;
        }

        return JsonBuilder.FromObject(new
        {
            requestId,
            response = new
            {
                url,
                status,
                statusText,
                headers = new Dictionary<string, string>()
            },
            frameId = contextId,
            type = "Document"
        });
    }
}

// ──────────────────────────────────────────────
// Target events
// ──────────────────────────────────────────────

/// <summary>
/// Target.attachedToTarget -> browsingContext.contextCreated
/// BiDi params: { context, url, ... }
/// CDP params: { sessionId, targetInfo: { targetId, type, url, ... }, waitingForDebugger }
/// </summary>
internal sealed class AttachedToTargetTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Target.attachedToTarget";
    public string BiDiEventName => "browsingContext.contextCreated";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var contextId = bidiParams.GetStringOrNull("context") ?? string.Empty;
        var url = bidiParams.GetStringOrNull("url") ?? string.Empty;

        return JsonBuilder.FromObject(new
        {
            sessionId = contextId,
            targetInfo = new
            {
                targetId = contextId,
                type = "page",
                title = string.Empty,
                url,
                attached = true,
                browserContextId = string.Empty
            },
            waitingForDebugger = false
        });
    }
}

/// <summary>
/// Target.detachedFromTarget -> browsingContext.contextDestroyed
/// BiDi params: { context }
/// CDP params: { sessionId, targetId? }
/// </summary>
internal sealed class DetachedFromTargetTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Target.detachedFromTarget";
    public string BiDiEventName => "browsingContext.contextDestroyed";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var contextId = bidiParams.GetStringOrNull("context") ?? string.Empty;
        return JsonBuilder.FromObject(new
        {
            sessionId = contextId,
            targetId = contextId
        });
    }
}

// ──────────────────────────────────────────────
// Console events
// ──────────────────────────────────────────────

/// <summary>
/// Runtime.consoleAPICalled -> log.entryAdded
/// BiDi params: { level, text, timestamp, ... }
/// CDP params: { type, args, executionContextId, timestamp, stackTrace? }
/// </summary>
internal sealed class ConsoleApiCalledTranslation : IBiDiEventTranslation
{
    public string CdpEventName => "Runtime.consoleAPICalled";
    public string BiDiEventName => "log.entryAdded";

    public JsonElement TranslateEvent(JsonElement bidiParams)
    {
        var level = bidiParams.GetStringOrNull("level") ?? "info";
        var text = bidiParams.GetStringOrNull("text") ?? string.Empty;
        var timestamp = bidiParams.GetDoubleOrNull("timestamp") ?? 0;

        // Map BiDi log level to CDP console type
        var cdpType = level switch
        {
            "error" => "error",
            "warn" or "warning" => "warning",
            "debug" => "debug",
            _ => "log"
        };

        return JsonBuilder.FromObject(new
        {
            type = cdpType,
            args = new[] { new { type = "string", value = text } },
            executionContextId = 0,
            timestamp
        });
    }
}
