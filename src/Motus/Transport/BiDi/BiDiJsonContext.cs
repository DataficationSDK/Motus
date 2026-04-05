using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Motus;

// ──────────────────────────────────────────────
// Session domain
// ──────────────────────────────────────────────

internal sealed record BiDiSessionStatusResult(
    [property: JsonPropertyName("ready")] bool Ready,
    [property: JsonPropertyName("message")] string Message);

internal sealed record BiDiSessionCapabilitiesRequest(
    [property: JsonPropertyName("alwaysMatch")] JsonElement? AlwaysMatch = null);

internal sealed record BiDiSessionNewParams(
    [property: JsonPropertyName("capabilities")] BiDiSessionCapabilitiesRequest Capabilities);

internal sealed record BiDiSessionNewResult(
    [property: JsonPropertyName("sessionId")] string SessionId,
    [property: JsonPropertyName("capabilities")] JsonElement? Capabilities = null);

internal sealed record BiDiSessionSubscribeParams(
    [property: JsonPropertyName("events")] string[] Events,
    [property: JsonPropertyName("contexts")] string[]? Contexts = null);

internal sealed record BiDiSessionSubscribeResult;

internal sealed record BiDiSessionEndResult;

// ──────────────────────────────────────────────
// BrowsingContext domain
// ──────────────────────────────────────────────

internal sealed record BiDiBrowsingContextCreateParams(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("referenceContext")] string? ReferenceContext = null);

internal sealed record BiDiBrowsingContextCreateResult(
    [property: JsonPropertyName("context")] string Context);

internal sealed record BiDiBrowsingContextNavigateParams(
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("wait")] string? Wait = null);

internal sealed record BiDiBrowsingContextNavigateResult(
    [property: JsonPropertyName("navigation")] string? Navigation,
    [property: JsonPropertyName("url")] string Url);

internal sealed record BiDiBrowsingContextCloseParams(
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("promptUnload")] bool? PromptUnload = null);

internal sealed record BiDiBrowsingContextCloseResult;

internal sealed record BiDiBrowsingContextReloadParams(
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("wait")] string? Wait = null);

internal sealed record BiDiBrowsingContextReloadResult;

// ──────────────────────────────────────────────
// Script domain
// ──────────────────────────────────────────────

internal sealed record BiDiScriptTarget(
    [property: JsonPropertyName("context")] string? Context = null,
    [property: JsonPropertyName("realm")] string? Realm = null);

internal sealed record BiDiScriptRemoteValue(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] JsonElement? Value = null,
    [property: JsonPropertyName("handle")] string? Handle = null);

internal sealed record BiDiScriptExceptionDetails(
    [property: JsonPropertyName("columnNumber")] long ColumnNumber,
    [property: JsonPropertyName("exception")] BiDiScriptRemoteValue Exception,
    [property: JsonPropertyName("lineNumber")] long LineNumber,
    [property: JsonPropertyName("text")] string Text);

internal sealed record BiDiScriptEvaluateParams(
    [property: JsonPropertyName("expression")] string Expression,
    [property: JsonPropertyName("target")] BiDiScriptTarget Target,
    [property: JsonPropertyName("awaitPromise")] bool AwaitPromise,
    [property: JsonPropertyName("resultOwnership")] string? ResultOwnership = null);

internal sealed record BiDiScriptEvaluateResult(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("result")] BiDiScriptRemoteValue? Result = null,
    [property: JsonPropertyName("exceptionDetails")] BiDiScriptExceptionDetails? ExceptionDetails = null);

internal sealed record BiDiScriptLocalValue(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] JsonElement? Value = null);

internal sealed record BiDiScriptCallFunctionParams(
    [property: JsonPropertyName("functionDeclaration")] string FunctionDeclaration,
    [property: JsonPropertyName("awaitPromise")] bool AwaitPromise,
    [property: JsonPropertyName("target")] BiDiScriptTarget Target,
    [property: JsonPropertyName("arguments")] BiDiScriptLocalValue[]? Arguments = null,
    [property: JsonPropertyName("resultOwnership")] string? ResultOwnership = null,
    [property: JsonPropertyName("this")] BiDiScriptLocalValue? This = null);

internal sealed record BiDiScriptCallFunctionResult(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("result")] BiDiScriptRemoteValue? Result = null,
    [property: JsonPropertyName("exceptionDetails")] BiDiScriptExceptionDetails? ExceptionDetails = null);

// ──────────────────────────────────────────────
// Input domain
// ──────────────────────────────────────────────

internal sealed record BiDiInputPerformActionsParams(
    [property: JsonPropertyName("context")] string Context,
    [property: JsonPropertyName("actions")] BiDiInputActionSequence[] Actions);

internal sealed record BiDiInputPerformActionsResult;

internal sealed record BiDiInputActionSequence(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("actions")] BiDiInputAction[] Actions,
    [property: JsonPropertyName("parameters")] BiDiInputSourceParameters? Parameters = null);

internal sealed record BiDiInputSourceParameters(
    [property: JsonPropertyName("pointerType")] string? PointerType = null);

internal sealed record BiDiInputAction(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("x")] double? X = null,
    [property: JsonPropertyName("y")] double? Y = null,
    [property: JsonPropertyName("duration")] long? Duration = null,
    [property: JsonPropertyName("button")] int? Button = null,
    [property: JsonPropertyName("origin")] string? Origin = null);

// ──────────────────────────────────────────────
// Network domain
// ──────────────────────────────────────────────

internal sealed record BiDiNetworkAddInterceptParams(
    [property: JsonPropertyName("phases")] string[] Phases,
    [property: JsonPropertyName("urlPatterns")] BiDiNetworkUrlPattern[]? UrlPatterns = null);

internal sealed record BiDiNetworkUrlPattern(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("pattern")] string? Pattern = null);

internal sealed record BiDiNetworkAddInterceptResult(
    [property: JsonPropertyName("intercept")] string Intercept);

internal sealed record BiDiNetworkContinueRequestParams(
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("url")] string? Url = null,
    [property: JsonPropertyName("method")] string? Method = null,
    [property: JsonPropertyName("body")] BiDiNetworkBytesValue? Body = null,
    [property: JsonPropertyName("headers")] BiDiNetworkHeader[]? Headers = null);

internal sealed record BiDiNetworkContinueRequestResult;

internal sealed record BiDiNetworkProvideResponseParams(
    [property: JsonPropertyName("request")] string Request,
    [property: JsonPropertyName("statusCode")] int? StatusCode = null,
    [property: JsonPropertyName("reasonPhrase")] string? ReasonPhrase = null,
    [property: JsonPropertyName("headers")] BiDiNetworkHeader[]? Headers = null,
    [property: JsonPropertyName("body")] BiDiNetworkBytesValue? Body = null);

internal sealed record BiDiNetworkProvideResponseResult;

internal sealed record BiDiNetworkFailRequestParams(
    [property: JsonPropertyName("request")] string Request);

internal sealed record BiDiNetworkFailRequestResult;

internal sealed record BiDiNetworkBytesValue(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("value")] string? Value = null);

internal sealed record BiDiNetworkHeader(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("value")] BiDiNetworkBytesValue Value);

// ──────────────────────────────────────────────
// Source-generated JSON context
// ──────────────────────────────────────────────

// Envelope types
[JsonSerializable(typeof(BiDiCommandEnvelope))]
[JsonSerializable(typeof(BiDiInboundDiscriminator))]
// Session domain
[JsonSerializable(typeof(BiDiSessionCapabilitiesRequest))]
[JsonSerializable(typeof(BiDiSessionNewParams))]
[JsonSerializable(typeof(BiDiSessionNewResult))]
[JsonSerializable(typeof(BiDiSessionStatusResult))]
[JsonSerializable(typeof(BiDiSessionSubscribeParams))]
[JsonSerializable(typeof(BiDiSessionSubscribeResult))]
[JsonSerializable(typeof(BiDiSessionEndResult))]
// BrowsingContext domain
[JsonSerializable(typeof(BiDiBrowsingContextCreateParams))]
[JsonSerializable(typeof(BiDiBrowsingContextCreateResult))]
[JsonSerializable(typeof(BiDiBrowsingContextNavigateParams))]
[JsonSerializable(typeof(BiDiBrowsingContextNavigateResult))]
[JsonSerializable(typeof(BiDiBrowsingContextCloseParams))]
[JsonSerializable(typeof(BiDiBrowsingContextCloseResult))]
[JsonSerializable(typeof(BiDiBrowsingContextReloadParams))]
[JsonSerializable(typeof(BiDiBrowsingContextReloadResult))]
// Script domain
[JsonSerializable(typeof(BiDiScriptTarget))]
[JsonSerializable(typeof(BiDiScriptRemoteValue))]
[JsonSerializable(typeof(BiDiScriptExceptionDetails))]
[JsonSerializable(typeof(BiDiScriptEvaluateParams))]
[JsonSerializable(typeof(BiDiScriptEvaluateResult))]
[JsonSerializable(typeof(BiDiScriptLocalValue))]
[JsonSerializable(typeof(BiDiScriptCallFunctionParams))]
[JsonSerializable(typeof(BiDiScriptCallFunctionResult))]
// Input domain
[JsonSerializable(typeof(BiDiInputPerformActionsParams))]
[JsonSerializable(typeof(BiDiInputPerformActionsResult))]
[JsonSerializable(typeof(BiDiInputActionSequence))]
[JsonSerializable(typeof(BiDiInputSourceParameters))]
[JsonSerializable(typeof(BiDiInputAction))]
// Network domain
[JsonSerializable(typeof(BiDiNetworkAddInterceptParams))]
[JsonSerializable(typeof(BiDiNetworkUrlPattern))]
[JsonSerializable(typeof(BiDiNetworkAddInterceptResult))]
[JsonSerializable(typeof(BiDiNetworkContinueRequestParams))]
[JsonSerializable(typeof(BiDiNetworkContinueRequestResult))]
[JsonSerializable(typeof(BiDiNetworkProvideResponseParams))]
[JsonSerializable(typeof(BiDiNetworkProvideResponseResult))]
[JsonSerializable(typeof(BiDiNetworkFailRequestParams))]
[JsonSerializable(typeof(BiDiNetworkFailRequestResult))]
[JsonSerializable(typeof(BiDiNetworkBytesValue))]
[JsonSerializable(typeof(BiDiNetworkHeader))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[ExcludeFromCodeCoverage]
internal sealed partial class BiDiJsonContext : JsonSerializerContext;
