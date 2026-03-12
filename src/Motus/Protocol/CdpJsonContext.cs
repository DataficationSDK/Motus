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
// --- Browser domain (permissions) ---
[JsonSerializable(typeof(BrowserGrantPermissionsParams))]
[JsonSerializable(typeof(BrowserGrantPermissionsResult))]
[JsonSerializable(typeof(BrowserResetPermissionsResult))]
// --- Emulation domain (geolocation) ---
[JsonSerializable(typeof(EmulationSetGeolocationOverrideParams))]
[JsonSerializable(typeof(EmulationSetGeolocationOverrideResult))]
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
// --- Page domain (clip) ---
[JsonSerializable(typeof(PageCaptureScreenshotWithClipParams))]
[JsonSerializable(typeof(PageClipRect))]
// --- Abstractions type registration ---
[JsonSerializable(typeof(Motus.Abstractions.BoundingBox))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
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
