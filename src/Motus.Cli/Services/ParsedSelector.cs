namespace Motus.Cli.Services;

/// <summary>
/// A Motus locator call extracted from a C# source file by <see cref="SelectorParser"/>.
/// </summary>
internal sealed record ParsedSelector(
    string Selector,
    string LocatorMethod,
    string SourceFile,
    int SourceLine,
    bool IsInterpolated);

/// <summary>
/// Aggregate result of parsing one or more C# source files for Motus locator calls.
/// </summary>
internal sealed record SelectorParseResult(
    IReadOnlyList<ParsedSelector> Selectors,
    IReadOnlyList<SelectorParseWarning> Warnings);

/// <summary>
/// Diagnostic emitted when a locator call cannot be statically extracted
/// (interpolated string, dynamic argument, etc.).
/// </summary>
internal sealed record SelectorParseWarning(
    string SourceFile,
    int SourceLine,
    string Message);
