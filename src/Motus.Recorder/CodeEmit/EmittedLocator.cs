namespace Motus.Recorder.CodeEmit;

/// <summary>
/// Metadata about a single locator call emitted into generated test/POM source,
/// used by selector manifest emission to map each locator to its source line.
/// </summary>
/// <param name="Selector">The selector string passed to the locator method.</param>
/// <param name="LocatorMethod">The locator factory method (e.g. "Locator", "GetByRole").</param>
/// <param name="SourceLine">1-based line number of the emitted call within the generated file.</param>
/// <param name="PageUrl">URL of the page at the time the locator was captured.</param>
/// <param name="BackendNodeId">CDP backend node id of the element, when known.</param>
public sealed record EmittedLocator(
    string Selector,
    string LocatorMethod,
    int SourceLine,
    string PageUrl,
    int? BackendNodeId);
