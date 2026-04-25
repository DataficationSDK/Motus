using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    /// <summary>
    /// The most recent code coverage snapshot, set by <see cref="CoverageCollector"/>
    /// when the page closes. Null when the hook is disabled or no collection has run.
    /// </summary>
    internal CoverageData? LastCoverage { get; set; }
}
