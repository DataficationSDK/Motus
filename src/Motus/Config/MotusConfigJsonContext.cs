using System.Text.Json.Serialization;

namespace Motus;

[JsonSerializable(typeof(MotusFailureConfig))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class MotusConfigJsonContext : JsonSerializerContext;
