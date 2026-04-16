using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Motus.Selectors;

[JsonSerializable(typeof(SelectorManifest))]
[JsonSerializable(typeof(SelectorEntry))]
[JsonSerializable(typeof(DomFingerprint))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[ExcludeFromCodeCoverage]
internal sealed partial class SelectorManifestJsonContext : JsonSerializerContext;
