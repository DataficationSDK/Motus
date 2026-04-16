namespace Motus.Recorder.CodeEmit;

/// <summary>
/// The output of a <see cref="CodeEmitter"/> call: the generated C# source plus
/// metadata about each locator call in the source (for manifest emission).
/// </summary>
public sealed record EmitResult(string Source, IReadOnlyList<EmittedLocator> Locators);
