using System.Text.Json;
using System.Text.Json.Serialization;

namespace Motus;

[JsonSerializable(typeof(CdpCommandEnvelope))]
[JsonSerializable(typeof(CdpInboundEnvelope))]
[JsonSerializable(typeof(CdpErrorPayload))]
[JsonSerializable(typeof(BrowserGetVersionResult))]
[JsonSerializable(typeof(BrowserCloseResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class CdpJsonContext : JsonSerializerContext;

/// <summary>
/// Result of the Browser.getVersion CDP command.
/// Manually defined to avoid cross-generator dependency with the CDP codegen.
/// </summary>
internal sealed record BrowserGetVersionResult(
    string ProtocolVersion,
    string Product,
    string Revision,
    string UserAgent,
    string JsVersion
);

/// <summary>
/// Result of the Browser.close CDP command (empty).
/// </summary>
internal sealed record BrowserCloseResult();
