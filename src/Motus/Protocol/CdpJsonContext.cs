using System.Text.Json;
using System.Text.Json.Serialization;

namespace Motus;

[JsonSerializable(typeof(CdpCommandEnvelope))]
[JsonSerializable(typeof(CdpInboundEnvelope))]
[JsonSerializable(typeof(CdpErrorPayload))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class CdpJsonContext : JsonSerializerContext;
