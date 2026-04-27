using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Motus;

[JsonSerializable(typeof(MotusRootConfig))]
[JsonSerializable(typeof(MotusFailureConfig))]
[JsonSerializable(typeof(MotusAssertionsConfig))]
[JsonSerializable(typeof(MotusLaunchConfig))]
[JsonSerializable(typeof(MotusContextConfig))]
[JsonSerializable(typeof(MotusViewportConfig))]
[JsonSerializable(typeof(MotusLocatorConfig))]
[JsonSerializable(typeof(MotusReporterConfig))]
[JsonSerializable(typeof(MotusRecorderConfig))]
[JsonSerializable(typeof(MotusAccessibilityConfig))]
[JsonSerializable(typeof(MotusPerformanceConfig))]
[JsonSerializable(typeof(MotusCoverageConfig))]
[JsonSerializable(typeof(MotusCoverageJsConfig))]
[JsonSerializable(typeof(MotusCoverageCssConfig))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[ExcludeFromCodeCoverage]
internal sealed partial class MotusConfigJsonContext : JsonSerializerContext;
