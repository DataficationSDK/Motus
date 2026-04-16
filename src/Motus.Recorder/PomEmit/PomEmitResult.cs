using Motus.Recorder.CodeEmit;

namespace Motus.Recorder.PomEmit;

/// <summary>
/// The output of a <see cref="PomEmitter"/> call: the generated C# source plus
/// metadata about each locator property (for manifest emission).
/// </summary>
public sealed record PomEmitResult(string Source, IReadOnlyList<EmittedLocator> Locators);
