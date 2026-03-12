using System.Text.Json.Serialization;

namespace Motus;

[JsonSerializable(typeof(MotusRootConfig))]
[JsonSerializable(typeof(MotusFailureConfig))]
[JsonSerializable(typeof(MotusAssertionsConfig))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class MotusConfigJsonContext : JsonSerializerContext;
