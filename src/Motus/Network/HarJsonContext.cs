using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Motus;

[JsonSerializable(typeof(HarLog))]
[JsonSerializable(typeof(HarCreator))]
[JsonSerializable(typeof(HarPage))]
[JsonSerializable(typeof(HarEntry))]
[JsonSerializable(typeof(HarRequest))]
[JsonSerializable(typeof(HarResponse))]
[JsonSerializable(typeof(HarHeader))]
[JsonSerializable(typeof(HarQueryParam))]
[JsonSerializable(typeof(HarPostData))]
[JsonSerializable(typeof(HarContent))]
[JsonSerializable(typeof(HarTimings))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[ExcludeFromCodeCoverage]
internal sealed partial class HarJsonContext : JsonSerializerContext;
