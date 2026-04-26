using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Motus;

// --- Existing types ---
[JsonSerializable(typeof(CdpCommandEnvelope))]
[JsonSerializable(typeof(CdpInboundEnvelope))]
[JsonSerializable(typeof(CdpErrorPayload))]
[JsonSerializable(typeof(BrowserGetVersionResult))]
[JsonSerializable(typeof(BrowserCloseResult))]
// --- Target domain ---
[JsonSerializable(typeof(TargetCreateBrowserContextParams))]
[JsonSerializable(typeof(TargetCreateBrowserContextResult))]
[JsonSerializable(typeof(TargetDisposeBrowserContextParams))]
[JsonSerializable(typeof(TargetDisposeBrowserContextResult))]
[JsonSerializable(typeof(TargetCreateTargetParams))]
[JsonSerializable(typeof(TargetCreateTargetResult))]
[JsonSerializable(typeof(TargetAttachToTargetParams))]
[JsonSerializable(typeof(TargetAttachToTargetResult))]
[JsonSerializable(typeof(TargetCloseTargetParams))]
[JsonSerializable(typeof(TargetCloseTargetResult))]
[JsonSerializable(typeof(TargetSetAutoAttachParams))]
[JsonSerializable(typeof(TargetSetAutoAttachResult))]
[JsonSerializable(typeof(TargetAttachedToTargetEvent))]
[JsonSerializable(typeof(TargetDetachedFromTargetEvent))]
[JsonSerializable(typeof(TargetTargetDestroyedEvent))]
// --- Page domain ---
[JsonSerializable(typeof(PageEnableResult))]
[JsonSerializable(typeof(PageNavigateParams))]
[JsonSerializable(typeof(PageNavigateResult))]
[JsonSerializable(typeof(PageReloadResult))]
[JsonSerializable(typeof(PageGetNavigationHistoryResult))]
[JsonSerializable(typeof(PageNavigateToHistoryEntryParams))]
[JsonSerializable(typeof(PageNavigateToHistoryEntryResult))]
[JsonSerializable(typeof(PageCaptureScreenshotParams))]
[JsonSerializable(typeof(PageCaptureScreenshotResult))]
[JsonSerializable(typeof(PagePrintToPdfResult))]
[JsonSerializable(typeof(PageBringToFrontResult))]
[JsonSerializable(typeof(PageHandleJavaScriptDialogParams))]
[JsonSerializable(typeof(PageHandleJavaScriptDialogResult))]
[JsonSerializable(typeof(PageSetInterceptFileChooserDialogParams))]
[JsonSerializable(typeof(PageSetInterceptFileChooserDialogResult))]
[JsonSerializable(typeof(PageAddScriptToEvaluateOnNewDocumentParams))]
[JsonSerializable(typeof(PageAddScriptToEvaluateOnNewDocumentResult))]
[JsonSerializable(typeof(PageLoadEventFiredEvent))]
[JsonSerializable(typeof(PageDomContentEventFiredEvent))]
[JsonSerializable(typeof(PageFrameNavigatedEvent))]
[JsonSerializable(typeof(PageFrameAttachedEvent))]
[JsonSerializable(typeof(PageFrameDetachedEvent))]
[JsonSerializable(typeof(PageJavascriptDialogOpeningEvent))]
[JsonSerializable(typeof(PageDownloadWillBeginEvent))]
[JsonSerializable(typeof(PageDownloadProgressEvent))]
[JsonSerializable(typeof(PageFileChooserOpenedEvent))]
// --- Runtime domain ---
[JsonSerializable(typeof(RuntimeEnableResult))]
[JsonSerializable(typeof(RuntimeEvaluateParams))]
[JsonSerializable(typeof(RuntimeEvaluateResult))]
[JsonSerializable(typeof(RuntimeCallFunctionOnParams))]
[JsonSerializable(typeof(RuntimeCallFunctionOnResult))]
[JsonSerializable(typeof(RuntimeGetPropertiesParams))]
[JsonSerializable(typeof(RuntimeGetPropertiesResult))]
[JsonSerializable(typeof(RuntimeReleaseObjectParams))]
[JsonSerializable(typeof(RuntimeReleaseObjectResult))]
[JsonSerializable(typeof(RuntimeAddBindingParams))]
[JsonSerializable(typeof(RuntimeAddBindingResult))]
[JsonSerializable(typeof(RuntimeConsoleApiCalledEvent))]
[JsonSerializable(typeof(RuntimeExceptionThrownEvent))]
[JsonSerializable(typeof(RuntimeBindingCalledEvent))]
[JsonSerializable(typeof(RuntimeExecutionContextCreatedEvent))]
// --- Emulation domain ---
[JsonSerializable(typeof(EmulationSetDeviceMetricsOverrideParams))]
[JsonSerializable(typeof(EmulationSetDeviceMetricsOverrideResult))]
// --- Network domain (cookies) ---
[JsonSerializable(typeof(NetworkGetCookiesParams))]
[JsonSerializable(typeof(NetworkGetCookiesResult))]
[JsonSerializable(typeof(NetworkSetCookieParams))]
[JsonSerializable(typeof(NetworkSetCookieResult))]
[JsonSerializable(typeof(NetworkClearBrowserCookiesResult))]
// --- Network domain (monitoring) ---
[JsonSerializable(typeof(NetworkEnableParams))]
[JsonSerializable(typeof(NetworkEnableResult))]
[JsonSerializable(typeof(NetworkGetResponseBodyParams))]
[JsonSerializable(typeof(NetworkGetResponseBodyResult))]
[JsonSerializable(typeof(NetworkSetExtraHttpHeadersParams))]
[JsonSerializable(typeof(NetworkSetExtraHttpHeadersResult))]
[JsonSerializable(typeof(NetworkEmulateNetworkConditionsParams))]
[JsonSerializable(typeof(NetworkEmulateNetworkConditionsResult))]
[JsonSerializable(typeof(NetworkRequestData))]
[JsonSerializable(typeof(NetworkResponseData))]
[JsonSerializable(typeof(NetworkRequestWillBeSentEvent))]
[JsonSerializable(typeof(NetworkResponseReceivedEvent))]
[JsonSerializable(typeof(NetworkLoadingFinishedEvent))]
[JsonSerializable(typeof(NetworkLoadingFailedEvent))]
// --- Fetch domain (interception) ---
[JsonSerializable(typeof(FetchEnableParams))]
[JsonSerializable(typeof(FetchEnableResult))]
[JsonSerializable(typeof(FetchDisableResult))]
[JsonSerializable(typeof(FetchRequestPattern))]
[JsonSerializable(typeof(FetchHeaderEntry))]
[JsonSerializable(typeof(FetchFulfillRequestParams))]
[JsonSerializable(typeof(FetchFulfillRequestResult))]
[JsonSerializable(typeof(FetchContinueRequestParams))]
[JsonSerializable(typeof(FetchContinueRequestResult))]
[JsonSerializable(typeof(FetchFailRequestParams))]
[JsonSerializable(typeof(FetchFailRequestResult))]
[JsonSerializable(typeof(FetchRequestPausedEvent))]
// --- Fetch domain (auth) ---
[JsonSerializable(typeof(FetchAuthChallenge))]
[JsonSerializable(typeof(FetchAuthChallengeResponse))]
[JsonSerializable(typeof(FetchContinueWithAuthParams))]
[JsonSerializable(typeof(FetchContinueWithAuthResult))]
[JsonSerializable(typeof(FetchAuthRequiredEvent))]
// --- Security domain ---
[JsonSerializable(typeof(SecurityEnableResult))]
[JsonSerializable(typeof(SecuritySetIgnoreCertificateErrorsParams))]
[JsonSerializable(typeof(SecuritySetIgnoreCertificateErrorsResult))]
// --- Browser domain (permissions) ---
[JsonSerializable(typeof(BrowserGrantPermissionsParams))]
[JsonSerializable(typeof(BrowserGrantPermissionsResult))]
[JsonSerializable(typeof(BrowserResetPermissionsResult))]
// --- Emulation domain (geolocation) ---
[JsonSerializable(typeof(EmulationSetGeolocationOverrideParams))]
[JsonSerializable(typeof(EmulationSetGeolocationOverrideResult))]
// --- Emulation domain (locale, timezone, user agent) ---
[JsonSerializable(typeof(EmulationSetLocaleOverrideParams))]
[JsonSerializable(typeof(EmulationSetLocaleOverrideResult))]
[JsonSerializable(typeof(EmulationSetTimezoneOverrideParams))]
[JsonSerializable(typeof(EmulationSetTimezoneOverrideResult))]
[JsonSerializable(typeof(EmulationSetUserAgentOverrideParams))]
[JsonSerializable(typeof(EmulationSetUserAgentOverrideResult))]
// --- Emulation domain (media) ---
[JsonSerializable(typeof(EmulationSetEmulatedMediaParams))]
[JsonSerializable(typeof(EmulationMediaFeature))]
[JsonSerializable(typeof(EmulationSetEmulatedMediaResult))]
// --- Input domain ---
[JsonSerializable(typeof(InputDispatchKeyEventParams))]
[JsonSerializable(typeof(InputDispatchKeyEventResult))]
[JsonSerializable(typeof(InputDispatchMouseEventParams))]
[JsonSerializable(typeof(InputDispatchMouseEventResult))]
[JsonSerializable(typeof(InputDispatchTouchEventParams))]
[JsonSerializable(typeof(InputDispatchTouchEventResult))]
[JsonSerializable(typeof(InputInsertTextParams))]
[JsonSerializable(typeof(InputInsertTextResult))]
// --- DOM domain ---
[JsonSerializable(typeof(DomEnableResult))]
[JsonSerializable(typeof(DomSetFileInputFilesParams))]
[JsonSerializable(typeof(DomSetFileInputFilesResult))]
[JsonSerializable(typeof(DomScrollIntoViewIfNeededParams))]
[JsonSerializable(typeof(DomScrollIntoViewIfNeededResult))]
[JsonSerializable(typeof(DomFocusParams))]
[JsonSerializable(typeof(DomFocusResult))]
[JsonSerializable(typeof(DomGetNodeForLocationParams))]
[JsonSerializable(typeof(DomGetNodeForLocationResult))]
[JsonSerializable(typeof(DomResolveNodeParams))]
[JsonSerializable(typeof(DomResolveNodeResult))]
// --- Page domain (screencast) ---
[JsonSerializable(typeof(PageStartScreencastParams))]
[JsonSerializable(typeof(PageStartScreencastResult))]
[JsonSerializable(typeof(PageStopScreencastResult))]
[JsonSerializable(typeof(PageScreencastFrameAckParams))]
[JsonSerializable(typeof(PageScreencastFrameAckResult))]
[JsonSerializable(typeof(PageScreencastFrameEvent))]
[JsonSerializable(typeof(PageScreencastFrameMetadata))]
// --- DOM domain (box model) ---
[JsonSerializable(typeof(DomGetBoxModelParams))]
[JsonSerializable(typeof(DomGetBoxModelResult))]
[JsonSerializable(typeof(DomBoxModel))]
// --- DOM domain (highlight) ---
[JsonSerializable(typeof(DomRgba))]
[JsonSerializable(typeof(DomHighlightConfig))]
[JsonSerializable(typeof(DomHighlightNodeParams))]
[JsonSerializable(typeof(DomHighlightNodeResult))]
[JsonSerializable(typeof(DomHideHighlightResult))]
// --- Page domain (clip) ---
[JsonSerializable(typeof(PageCaptureScreenshotWithClipParams))]
[JsonSerializable(typeof(PageClipRect))]
// --- Accessibility domain ---
[JsonSerializable(typeof(AccessibilityEnableResult))]
[JsonSerializable(typeof(AccessibilityQueryAXTreeParams))]
[JsonSerializable(typeof(AccessibilityQueryAXTreeResult))]
[JsonSerializable(typeof(AccessibilityGetFullAXTreeParams))]
[JsonSerializable(typeof(AccessibilityGetFullAXTreeResult))]
[JsonSerializable(typeof(AccessibilityAXNode))]
[JsonSerializable(typeof(AccessibilityAXValue))]
[JsonSerializable(typeof(AccessibilityAXProperty))]
[JsonSerializable(typeof(AccessibilityAXRelatedNode))]
// --- DOM domain ---
[JsonSerializable(typeof(DomDescribeNodeParams))]
[JsonSerializable(typeof(DomDescribeNodeResult))]
[JsonSerializable(typeof(DomNodeDescription))]
[JsonSerializable(typeof(DomGetDocumentParams))]
[JsonSerializable(typeof(DomGetDocumentResult))]
[JsonSerializable(typeof(DomDocumentNode))]
[JsonSerializable(typeof(DomQuerySelectorAllParams))]
[JsonSerializable(typeof(DomQuerySelectorAllResult))]
[JsonSerializable(typeof(DomGetAttributesParams))]
[JsonSerializable(typeof(DomGetAttributesResult))]
[JsonSerializable(typeof(DomPushNodesByBackendIdsParams))]
[JsonSerializable(typeof(DomPushNodesByBackendIdsResult))]
[JsonSerializable(typeof(DomGetOuterHtmlParams))]
[JsonSerializable(typeof(DomGetOuterHtmlResult))]
// --- CSS domain ---
[JsonSerializable(typeof(CssEnableResult))]
[JsonSerializable(typeof(CssGetComputedStyleForNodeParams))]
[JsonSerializable(typeof(CssGetComputedStyleForNodeResult))]
[JsonSerializable(typeof(CssComputedStyleProperty))]
// --- CSS domain (rule usage tracking) ---
[JsonSerializable(typeof(CssStartRuleUsageTrackingResult))]
[JsonSerializable(typeof(CssStopRuleUsageTrackingResult))]
[JsonSerializable(typeof(CssRuleUsageEntry))]
[JsonSerializable(typeof(CssGetStyleSheetTextParams))]
[JsonSerializable(typeof(CssGetStyleSheetTextResult))]
// --- Profiler domain ---
[JsonSerializable(typeof(ProfilerEnableResult))]
[JsonSerializable(typeof(ProfilerStartPreciseCoverageParams))]
[JsonSerializable(typeof(ProfilerStartPreciseCoverageResult))]
[JsonSerializable(typeof(ProfilerTakePreciseCoverageResult))]
[JsonSerializable(typeof(ProfilerStopPreciseCoverageResult))]
[JsonSerializable(typeof(ProfilerScriptCoverage))]
[JsonSerializable(typeof(ProfilerFunctionCoverage))]
[JsonSerializable(typeof(ProfilerCoverageRange))]
[JsonSerializable(typeof(ProfilerGetScriptSourceParams))]
[JsonSerializable(typeof(ProfilerGetScriptSourceResult))]
// --- Source map document ---
[JsonSerializable(typeof(SourceMapJsonDto))]
// --- DOMDebugger domain ---
[JsonSerializable(typeof(DomDebuggerGetEventListenersParams))]
[JsonSerializable(typeof(DomDebuggerGetEventListenersResult))]
[JsonSerializable(typeof(DomDebuggerEventListener))]
// --- Performance domain ---
[JsonSerializable(typeof(PerformanceEnableResult))]
[JsonSerializable(typeof(PerformanceGetMetricsResult))]
[JsonSerializable(typeof(PerformanceMetricEntry))]
// --- Tracing domain ---
[JsonSerializable(typeof(TracingStartParams))]
[JsonSerializable(typeof(TracingStartResult))]
[JsonSerializable(typeof(TracingEndResult))]
[JsonSerializable(typeof(TracingTraceConfig))]
[JsonSerializable(typeof(TracingDataCollectedEvent))]
[JsonSerializable(typeof(TracingTracingCompleteEvent))]
// --- IO domain ---
[JsonSerializable(typeof(IoReadParams))]
[JsonSerializable(typeof(IoReadResult))]
[JsonSerializable(typeof(IoCloseParams))]
[JsonSerializable(typeof(IoCloseResult))]
// --- Abstractions type registration ---
[JsonSerializable(typeof(Motus.Abstractions.BoundingBox))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[ExcludeFromCodeCoverage]
internal sealed partial class CdpJsonContext : JsonSerializerContext;

// ============================================================================
// Browser domain
// ============================================================================

internal sealed record BrowserGetVersionResult(
    string ProtocolVersion,
    string Product,
    string Revision,
    string UserAgent,
    string JsVersion);

internal sealed record BrowserCloseResult();

internal sealed record BrowserGrantPermissionsParams(
    string[] Permissions,
    string? Origin = null,
    string? BrowserContextId = null);

internal sealed record BrowserGrantPermissionsResult();

internal sealed record BrowserResetPermissionsResult();

// ============================================================================
// Target domain
// ============================================================================

internal sealed record TargetCreateBrowserContextParams(
    bool? DisposeOnDetach = null,
    string? ProxyServer = null);

internal sealed record TargetCreateBrowserContextResult(string BrowserContextId);

internal sealed record TargetDisposeBrowserContextParams(string BrowserContextId);

internal sealed record TargetDisposeBrowserContextResult();

internal sealed record TargetCreateTargetParams(
    string Url,
    string? BrowserContextId = null,
    int? Width = null,
    int? Height = null);

internal sealed record TargetCreateTargetResult(string TargetId);

internal sealed record TargetAttachToTargetParams(string TargetId, bool Flatten = true);

internal sealed record TargetAttachToTargetResult(string SessionId);

internal sealed record TargetCloseTargetParams(string TargetId);

internal sealed record TargetCloseTargetResult(bool Success);

internal sealed record TargetSetAutoAttachParams(
    bool AutoAttach,
    bool WaitForDebuggerOnStart,
    bool? Flatten = null);

internal sealed record TargetSetAutoAttachResult();

internal sealed record TargetInfo(
    string TargetId,
    string Type,
    string Title,
    string Url,
    bool Attached,
    string? BrowserContextId = null,
    string? OpenerId = null);

internal sealed record TargetAttachedToTargetEvent(
    string SessionId,
    TargetInfo TargetInfo,
    bool WaitingForDebugger);

internal sealed record TargetDetachedFromTargetEvent(
    string SessionId,
    string? TargetId = null);

internal sealed record TargetTargetDestroyedEvent(string TargetId);

// ============================================================================
// Page domain
// ============================================================================

internal sealed record PageEnableResult();

internal sealed record PageNavigateParams(
    string Url,
    string? Referrer = null,
    string? FrameId = null);

internal sealed record PageNavigateResult(
    string FrameId,
    string? LoaderId = null,
    string? ErrorText = null);

internal sealed record PageReloadResult();

internal sealed record PageHistoryEntry(int Id, string Url, string UserTypedURL, string Title);

internal sealed record PageGetNavigationHistoryResult(
    int CurrentIndex,
    PageHistoryEntry[] Entries);

internal sealed record PageNavigateToHistoryEntryParams(int EntryId);

internal sealed record PageNavigateToHistoryEntryResult();

internal sealed record PageCaptureScreenshotParams(
    string? Format = null,
    int? Quality = null,
    bool? FromSurface = null,
    bool? CaptureBeyondViewport = null);

internal sealed record PageCaptureScreenshotResult(string Data);

internal sealed record PagePrintToPdfResult(string Data);

internal sealed record PageBringToFrontResult();

internal sealed record PageHandleJavaScriptDialogParams(bool Accept, string? PromptText = null);

internal sealed record PageHandleJavaScriptDialogResult();

internal sealed record PageSetInterceptFileChooserDialogParams(bool Enabled);

internal sealed record PageSetInterceptFileChooserDialogResult();

internal sealed record PageAddScriptToEvaluateOnNewDocumentParams(string Source);

internal sealed record PageAddScriptToEvaluateOnNewDocumentResult(string Identifier);

internal sealed record PageFrameInfo(
    string Id,
    string? ParentId,
    string? LoaderId,
    string Name,
    string Url,
    string? SecurityOrigin = null,
    string? MimeType = null);

internal sealed record PageLoadEventFiredEvent(double Timestamp);

internal sealed record PageDomContentEventFiredEvent(double Timestamp);

internal sealed record PageFrameNavigatedEvent(PageFrameInfo Frame);

internal sealed record PageFrameAttachedEvent(string FrameId, string ParentFrameId);

internal sealed record PageFrameDetachedEvent(string FrameId, string? Reason = null);

internal sealed record PageJavascriptDialogOpeningEvent(
    string Url,
    string Message,
    string Type,
    bool HasBrowserHandler,
    string? DefaultPrompt = null);

internal sealed record PageDownloadWillBeginEvent(
    string FrameId,
    string Guid,
    string Url,
    string SuggestedFilename);

internal sealed record PageDownloadProgressEvent(
    string Guid,
    double TotalBytes,
    double ReceivedBytes,
    string State);

internal sealed record PageFileChooserOpenedEvent(
    string FrameId,
    int BackendNodeId,
    string Mode);

// ============================================================================
// Runtime domain
// ============================================================================

internal sealed record RuntimeEnableResult();

internal sealed record RuntimeRemoteObject(
    string Type,
    string? Subtype = null,
    string? ClassName = null,
    JsonElement? Value = null,
    string? UnserializableValue = null,
    string? Description = null,
    string? ObjectId = null);

internal sealed record RuntimeExceptionDetails(
    int ExceptionId,
    string Text,
    int LineNumber,
    int ColumnNumber,
    string? Url = null,
    RuntimeRemoteObject? Exception = null);

internal sealed record RuntimeCallArgument(
    JsonElement? Value = null,
    string? UnserializableValue = null,
    string? ObjectId = null);

internal sealed record RuntimeEvaluateParams(
    string Expression,
    bool? ReturnByValue = null,
    bool? AwaitPromise = null,
    int? ContextId = null,
    bool? UserGesture = null);

internal sealed record RuntimeEvaluateResult(
    RuntimeRemoteObject Result,
    RuntimeExceptionDetails? ExceptionDetails = null);

internal sealed record RuntimeCallFunctionOnParams(
    string FunctionDeclaration,
    string? ObjectId = null,
    RuntimeCallArgument[]? Arguments = null,
    bool? ReturnByValue = null,
    bool? AwaitPromise = null);

internal sealed record RuntimeCallFunctionOnResult(
    RuntimeRemoteObject Result,
    RuntimeExceptionDetails? ExceptionDetails = null);

internal sealed record RuntimeGetPropertiesParams(
    string ObjectId,
    bool? OwnProperties = null,
    bool? AccessorPropertiesOnly = null);

internal sealed record RuntimePropertyDescriptor(
    string Name,
    RuntimeRemoteObject? Value = null,
    bool? Writable = null,
    RuntimeRemoteObject? Get = null,
    RuntimeRemoteObject? Set = null,
    bool? Configurable = null,
    bool? Enumerable = null,
    bool? WasThrown = null,
    bool? IsOwn = null);

internal sealed record RuntimeGetPropertiesResult(RuntimePropertyDescriptor[] Result);

internal sealed record RuntimeReleaseObjectParams(string ObjectId);

internal sealed record RuntimeReleaseObjectResult();

internal sealed record RuntimeAddBindingParams(string Name, int? ExecutionContextId = null);

internal sealed record RuntimeAddBindingResult();

internal sealed record RuntimeExecutionContextDescription(
    int Id,
    string Origin,
    string Name,
    string? UniqueId = null,
    JsonElement? AuxData = null);

internal sealed record RuntimeExecutionContextCreatedEvent(
    RuntimeExecutionContextDescription Context);

internal sealed record RuntimeConsoleApiCalledEvent(
    string Type,
    RuntimeRemoteObject[] Args,
    int ExecutionContextId,
    double Timestamp);

internal sealed record RuntimeExceptionThrownEvent(
    double Timestamp,
    RuntimeExceptionDetails ExceptionDetails);

internal sealed record RuntimeBindingCalledEvent(
    string Name,
    string Payload,
    int ExecutionContextId);

// ============================================================================
// Emulation domain
// ============================================================================

internal sealed record EmulationSetDeviceMetricsOverrideParams(
    int Width,
    int Height,
    double DeviceScaleFactor,
    bool Mobile);

internal sealed record EmulationSetDeviceMetricsOverrideResult();

internal sealed record EmulationSetGeolocationOverrideParams(
    double? Latitude = null,
    double? Longitude = null,
    double? Accuracy = null);

internal sealed record EmulationSetGeolocationOverrideResult();

// ============================================================================
// Network domain (cookies)
// ============================================================================

internal sealed record NetworkGetCookiesParams(string[]? Urls = null);

internal sealed record NetworkCookieData(
    string Name,
    string Value,
    string Domain,
    string Path,
    double Expires,
    int Size,
    bool HttpOnly,
    bool Secure,
    string SameSite,
    string? Priority = null,
    bool? SameParty = null);

internal sealed record NetworkGetCookiesResult(NetworkCookieData[] Cookies);

internal sealed record NetworkSetCookieParams(
    string Name,
    string Value,
    string? Url = null,
    string? Domain = null,
    string? Path = null,
    bool? Secure = null,
    bool? HttpOnly = null,
    string? SameSite = null,
    double? Expires = null);

internal sealed record NetworkSetCookieResult(bool Success);

internal sealed record NetworkClearBrowserCookiesResult();

// ============================================================================
// Input domain
// ============================================================================

internal sealed record InputDispatchKeyEventParams(
    string Type,
    int? Modifiers = null,
    string? Text = null,
    string? UnmodifiedText = null,
    string? Code = null,
    string? Key = null,
    int? WindowsVirtualKeyCode = null,
    int? NativeVirtualKeyCode = null,
    bool? AutoRepeat = null,
    bool? IsKeypad = null,
    bool? IsSystemKey = null,
    int? Location = null);

internal sealed record InputDispatchKeyEventResult();

internal sealed record InputDispatchMouseEventParams(
    string Type,
    double X,
    double Y,
    int? Modifiers = null,
    string? Button = null,
    int? Buttons = null,
    int? ClickCount = null,
    double? DeltaX = null,
    double? DeltaY = null,
    string? PointerType = null);

internal sealed record InputDispatchMouseEventResult();

internal sealed record InputDispatchTouchEventParams(
    string Type,
    InputTouchPoint[] TouchPoints,
    int? Modifiers = null);

internal sealed record InputDispatchTouchEventResult();

internal sealed record InputTouchPoint(
    double X,
    double Y,
    double? RadiusX = null,
    double? RadiusY = null,
    double? Force = null,
    int? Id = null);

internal sealed record InputInsertTextParams(string Text);

internal sealed record InputInsertTextResult();

// ============================================================================
// DOM domain
// ============================================================================

internal sealed record DomEnableResult();

internal sealed record DomSetFileInputFilesParams(
    string[] Files,
    string? ObjectId = null);

internal sealed record DomSetFileInputFilesResult();

internal sealed record DomScrollIntoViewIfNeededParams(string? ObjectId = null);

internal sealed record DomScrollIntoViewIfNeededResult();

internal sealed record DomFocusParams(string? ObjectId = null);

internal sealed record DomFocusResult();

internal sealed record DomGetNodeForLocationParams(
    int X,
    int Y,
    bool? IncludeUserAgentShadowDOM = null,
    bool? IgnorePointerEventsNone = null);

internal sealed record DomGetNodeForLocationResult(
    int BackendNodeId,
    int? NodeId = null,
    string? FrameId = null);

internal sealed record DomResolveNodeParams(
    int? BackendNodeId = null,
    string? ObjectGroup = null);

internal sealed record DomResolveNodeResult(RuntimeRemoteObject Object);

internal sealed record DomDescribeNodeParams(
    int? BackendNodeId = null,
    int? NodeId = null,
    string? ObjectId = null,
    bool? Pierce = null);

internal sealed record DomNodeDescription(
    string? LocalName = null,
    string? NodeName = null,
    int? NodeId = null,
    int? ParentId = null,
    int? BackendNodeId = null,
    JsonElement? Attributes = null);

internal sealed record DomDescribeNodeResult(DomNodeDescription Node);

internal sealed record DomGetDocumentParams(int? Depth = null, bool? Pierce = null);

internal sealed record DomDocumentNode(
    int NodeId,
    int BackendNodeId,
    string? NodeName = null,
    string? NodeValue = null,
    int? ChildNodeCount = null);

internal sealed record DomGetDocumentResult(DomDocumentNode Root);

internal sealed record DomQuerySelectorAllParams(int NodeId, string Selector);

internal sealed record DomQuerySelectorAllResult(int[] NodeIds);

internal sealed record DomGetAttributesParams(int NodeId);

internal sealed record DomGetAttributesResult(string[] Attributes);

internal sealed record DomPushNodesByBackendIdsParams(int[] BackendNodeIds);

internal sealed record DomPushNodesByBackendIdsResult(int[] NodeIds);

internal sealed record DomGetOuterHtmlParams(
    int? NodeId = null,
    int? BackendNodeId = null,
    string? ObjectId = null);

internal sealed record DomGetOuterHtmlResult(string OuterHTML);

// ============================================================================
// CSS domain
// ============================================================================

internal sealed record CssEnableResult();

internal sealed record CssGetComputedStyleForNodeParams(int NodeId);

internal sealed record CssComputedStyleProperty(string Name, string Value);

internal sealed record CssGetComputedStyleForNodeResult(CssComputedStyleProperty[] ComputedStyle);

// ============================================================================
// CSS domain (rule usage tracking)
// ============================================================================

internal sealed record CssStartRuleUsageTrackingResult();

internal sealed record CssRuleUsageEntry(
    string StyleSheetId,
    int StartOffset,
    int EndOffset,
    bool Used);

internal sealed record CssStopRuleUsageTrackingResult(CssRuleUsageEntry[] RuleUsage);

internal sealed record CssGetStyleSheetTextParams(string StyleSheetId);

internal sealed record CssGetStyleSheetTextResult(string Text);

// ============================================================================
// Profiler domain
// ============================================================================

internal sealed record ProfilerEnableResult();

internal sealed record ProfilerStartPreciseCoverageParams(
    bool? CallCount = null,
    bool? Detailed = null,
    bool? AllowTriggeredUpdates = null);

internal sealed record ProfilerStartPreciseCoverageResult(double? Timestamp = null);

internal sealed record ProfilerCoverageRange(
    int StartOffset,
    int EndOffset,
    int Count);

internal sealed record ProfilerFunctionCoverage(
    string FunctionName,
    ProfilerCoverageRange[] Ranges,
    bool IsBlockCoverage);

internal sealed record ProfilerScriptCoverage(
    string ScriptId,
    string Url,
    ProfilerFunctionCoverage[] Functions);

internal sealed record ProfilerTakePreciseCoverageResult(
    ProfilerScriptCoverage[] Result,
    double? Timestamp = null);

internal sealed record ProfilerStopPreciseCoverageResult();

internal sealed record ProfilerGetScriptSourceParams(string ScriptId);

internal sealed record ProfilerGetScriptSourceResult(string ScriptSource);

// ============================================================================
// Page domain (screenshot with clip)
// ============================================================================

internal sealed record PageClipRect(
    double X,
    double Y,
    double Width,
    double Height,
    double Scale = 1);

internal sealed record PageCaptureScreenshotWithClipParams(
    PageClipRect Clip,
    string? Format = null,
    int? Quality = null,
    bool? FromSurface = null,
    bool? CaptureBeyondViewport = null);

// ============================================================================
// Emulation domain (media)
// ============================================================================

internal sealed record EmulationSetEmulatedMediaParams(
    string? Media = null,
    EmulationMediaFeature[]? Features = null);

internal sealed record EmulationMediaFeature(string Name, string Value);

internal sealed record EmulationSetEmulatedMediaResult();

// ============================================================================
// Emulation domain (locale, timezone, user agent)
// ============================================================================

internal sealed record EmulationSetLocaleOverrideParams(string? Locale);

internal sealed record EmulationSetLocaleOverrideResult();

internal sealed record EmulationSetTimezoneOverrideParams(string TimezoneId);

internal sealed record EmulationSetTimezoneOverrideResult();

internal sealed record EmulationSetUserAgentOverrideParams(
    string UserAgent,
    string? AcceptLanguage = null,
    string? Platform = null);

internal sealed record EmulationSetUserAgentOverrideResult();

// ============================================================================
// Security domain
// ============================================================================

internal sealed record SecurityEnableResult();

internal sealed record SecuritySetIgnoreCertificateErrorsParams(bool Ignore);

internal sealed record SecuritySetIgnoreCertificateErrorsResult();

// ============================================================================
// Accessibility domain
// ============================================================================

internal sealed record AccessibilityEnableResult();

internal sealed record AccessibilityQueryAXTreeParams(
    string? ObjectId = null,
    string? AccessibleName = null,
    string? Role = null);

internal sealed record AccessibilityAXValueSimple(
    string Type,
    JsonElement? Value = null);

internal sealed record AccessibilityAXNodeSimple(
    string NodeId,
    bool Ignored,
    AccessibilityAXValueSimple? Role = null,
    AccessibilityAXValueSimple? Name = null,
    long? BackendDOMNodeId = null);

internal sealed record AccessibilityQueryAXTreeResult(AccessibilityAXNodeSimple[] Nodes);

internal sealed record AccessibilityGetFullAXTreeParams(
    int? Depth = null,
    string? FrameId = null);

internal sealed record AccessibilityAXRelatedNode(
    long? BackendDOMNodeId = null,
    string? Idref = null,
    string? Text = null);

internal sealed record AccessibilityAXValue(
    string Type,
    JsonElement? Value = null,
    AccessibilityAXRelatedNode[]? RelatedNodes = null);

internal sealed record AccessibilityAXProperty(
    string Name,
    AccessibilityAXValue Value);

internal sealed record AccessibilityAXNode(
    string NodeId,
    bool Ignored,
    AccessibilityAXValue? Role = null,
    AccessibilityAXValue? Name = null,
    AccessibilityAXValue? Description = null,
    AccessibilityAXValue? Value = null,
    AccessibilityAXProperty[]? Properties = null,
    string[]? ChildIds = null,
    long? BackendDOMNodeId = null,
    string? ParentId = null);

internal sealed record AccessibilityGetFullAXTreeResult(AccessibilityAXNode[] Nodes);

// ============================================================================
// Network domain (monitoring)
// ============================================================================

internal sealed record NetworkEnableParams(
    int? MaxTotalBufferSize = null,
    int? MaxResourceBufferSize = null,
    int? MaxPostDataSize = null);

internal sealed record NetworkEnableResult();

internal sealed record NetworkGetResponseBodyParams(string RequestId);

internal sealed record NetworkGetResponseBodyResult(string Body, bool Base64Encoded);

internal sealed record NetworkSetExtraHttpHeadersParams(
    Dictionary<string, string> Headers);

internal sealed record NetworkSetExtraHttpHeadersResult();

internal sealed record NetworkEmulateNetworkConditionsParams(
    bool Offline,
    double Latency,
    double DownloadThroughput,
    double UploadThroughput);

internal sealed record NetworkEmulateNetworkConditionsResult();

internal sealed record NetworkRequestData(
    string Url,
    string Method,
    Dictionary<string, string>? Headers = null,
    string? PostData = null);

internal sealed record NetworkResponseData(
    string Url,
    int Status,
    string StatusText,
    Dictionary<string, string>? Headers = null,
    string? MimeType = null);

internal sealed record NetworkRequestWillBeSentEvent(
    string RequestId,
    string LoaderId,
    string DocumentUrl,
    NetworkRequestData Request,
    double Timestamp,
    double WallTime,
    string? FrameId = null,
    string? Type = null);

internal sealed record NetworkResponseReceivedEvent(
    string RequestId,
    string LoaderId,
    double Timestamp,
    string? Type = null,
    NetworkResponseData? Response = null,
    string? FrameId = null);

internal sealed record NetworkLoadingFinishedEvent(
    string RequestId,
    double Timestamp,
    double EncodedDataLength);

internal sealed record NetworkLoadingFailedEvent(
    string RequestId,
    double Timestamp,
    string Type,
    string ErrorText,
    bool? Canceled = null);

// ============================================================================
// Fetch domain (interception)
// ============================================================================

internal sealed record FetchRequestPattern(
    string? UrlPattern = null,
    string? ResourceType = null,
    string? RequestStage = null);

internal sealed record FetchEnableParams(
    FetchRequestPattern[]? Patterns = null,
    bool? HandleAuthRequests = null);

internal sealed record FetchEnableResult();

internal sealed record FetchDisableResult();

internal sealed record FetchHeaderEntry(string Name, string Value);

internal sealed record FetchFulfillRequestParams(
    string RequestId,
    int ResponseCode,
    FetchHeaderEntry[]? ResponseHeaders = null,
    string? Body = null,
    string? ResponsePhrase = null);

internal sealed record FetchFulfillRequestResult();

internal sealed record FetchContinueRequestParams(
    string RequestId,
    string? Url = null,
    string? Method = null,
    string? PostData = null,
    FetchHeaderEntry[]? Headers = null);

internal sealed record FetchContinueRequestResult();

internal sealed record FetchFailRequestParams(
    string RequestId,
    string ErrorReason);

internal sealed record FetchFailRequestResult();

internal sealed record FetchRequestPausedEvent(
    string RequestId,
    NetworkRequestData Request,
    string FrameId,
    string ResourceType,
    int? ResponseStatusCode = null,
    string? ResponseStatusText = null,
    FetchHeaderEntry[]? ResponseHeaders = null,
    string? NetworkId = null);

// ============================================================================
// Fetch domain (auth)
// ============================================================================

internal sealed record FetchAuthChallenge(
    string Source,
    string Origin,
    string Scheme,
    string Realm);

internal sealed record FetchAuthChallengeResponse(
    string Response,
    string? Username = null,
    string? Password = null);

internal sealed record FetchContinueWithAuthParams(
    string RequestId,
    FetchAuthChallengeResponse AuthChallengeResponse);

internal sealed record FetchContinueWithAuthResult();

internal sealed record FetchAuthRequiredEvent(
    string RequestId,
    string FrameId,
    string ResourceType,
    string? NetworkId,
    FetchAuthChallenge AuthChallenge);

// ============================================================================
// Page domain (screencast)
// ============================================================================

internal sealed record PageStartScreencastParams(
    string? Format = null,
    int? Quality = null,
    int? MaxWidth = null,
    int? MaxHeight = null,
    int? EveryNthFrame = null);

internal sealed record PageStartScreencastResult();

internal sealed record PageStopScreencastResult();

internal sealed record PageScreencastFrameAckParams(int SessionId);

internal sealed record PageScreencastFrameAckResult();

internal sealed record PageScreencastFrameMetadata(
    double OffsetTop,
    double PageScaleFactor,
    double DeviceWidth,
    double DeviceHeight,
    double ScrollOffsetX,
    double ScrollOffsetY,
    double? Timestamp = null);

internal sealed record PageScreencastFrameEvent(
    string Data,
    PageScreencastFrameMetadata Metadata,
    int SessionId);

// ============================================================================
// DOM domain (box model)
// ============================================================================

internal sealed record DomGetBoxModelParams(
    string? ObjectId = null,
    int? BackendNodeId = null,
    int? NodeId = null);

internal sealed record DomBoxModel(
    double[] Content,
    double[] Padding,
    double[] Border,
    double[] Margin,
    double Width,
    double Height);

internal sealed record DomGetBoxModelResult(DomBoxModel Model);

// ============================================================================
// DOM domain (highlight)
// ============================================================================

internal sealed record DomRgba(int R, int G, int B, double? A = null);

internal sealed record DomHighlightConfig(
    bool? ShowInfo = null,
    DomRgba? ContentColor = null,
    DomRgba? PaddingColor = null,
    DomRgba? BorderColor = null,
    DomRgba? MarginColor = null);

internal sealed record DomHighlightNodeParams(
    DomHighlightConfig HighlightConfig,
    int? BackendNodeId = null,
    int? NodeId = null,
    string? ObjectId = null);

internal sealed record DomHighlightNodeResult();

internal sealed record DomHideHighlightResult();

// ============================================================================
// Tracing domain
// ============================================================================

internal sealed record TracingTraceConfig(
    string? RecordMode = null,
    string[]? IncludedCategories = null,
    string[]? ExcludedCategories = null);

internal sealed record TracingStartParams(
    string? Categories = null,
    string? TransferMode = null,
    string? StreamFormat = null,
    string? StreamCompression = null,
    double? BufferUsageReportingInterval = null,
    TracingTraceConfig? TraceConfig = null);

internal sealed record TracingStartResult();

internal sealed record TracingEndResult();

internal sealed record TracingDataCollectedEvent(JsonElement[] Value);

internal sealed record TracingTracingCompleteEvent(
    bool DataLossOccurred,
    string? Stream = null,
    string? TraceFormat = null,
    string? StreamCompression = null);

// ============================================================================
// IO domain
// ============================================================================

internal sealed record IoReadParams(
    string Handle,
    int? Offset = null,
    int? Size = null);

internal sealed record IoReadResult(
    string Data,
    bool Base64Encoded,
    bool Eof);

internal sealed record IoCloseParams(string Handle);

internal sealed record IoCloseResult();

// ============================================================================
// Performance domain
// ============================================================================

internal sealed record PerformanceEnableResult();

internal sealed record PerformanceMetricEntry(string Name, double Value);

internal sealed record PerformanceGetMetricsResult(PerformanceMetricEntry[] Metrics);

// ============================================================================
// DOMDebugger domain
// ============================================================================

internal sealed record DomDebuggerGetEventListenersParams(
    string ObjectId,
    int? Depth = null,
    bool? Pierce = null);

internal sealed record DomDebuggerEventListener(
    string Type,
    bool UseCapture,
    bool Passive,
    bool Once,
    string ScriptId,
    long LineNumber,
    long ColumnNumber,
    long? BackendNodeId = null);

internal sealed record DomDebuggerGetEventListenersResult(
    DomDebuggerEventListener[] Listeners);
