namespace Motus.Recorder.CodeEmit;

/// <summary>
/// Configuration for <see cref="CodeEmitter"/>.
/// </summary>
public sealed class CodeEmitOptions
{
    /// <summary>Test framework: "mstest", "xunit", or "nunit".</summary>
    public string Framework { get; init; } = "mstest";

    /// <summary>Name of the generated test class.</summary>
    public string TestClassName { get; init; } = "RecordedTest";

    /// <summary>Name of the generated test method.</summary>
    public string TestMethodName { get; init; } = "RecordedScenario";

    /// <summary>Namespace for the generated code.</summary>
    public string Namespace { get; init; } = "Motus.Generated";
}
