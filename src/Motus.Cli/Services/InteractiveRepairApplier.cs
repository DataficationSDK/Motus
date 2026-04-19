using Motus.Runner.Services.SelectorRepair;

namespace Motus.Cli.Services;

/// <summary>
/// Constructs a single-item <see cref="SelectorCheckResult"/> with a synthetic
/// <see cref="Confidence.High"/> suggestion and runs <see cref="SelectorRewriter"/>
/// on it. Extracted from <see cref="CheckSelectorsRunner"/>'s interactive flow so
/// the apply path is unit-testable without spinning up the runner UI.
/// </summary>
internal static class InteractiveRepairApplier
{
    internal static RepairOutcome Apply(
        RepairQueueItem item, string replacement, bool backup, CancellationToken ct)
    {
        var single = new List<SelectorCheckResult>
        {
            new(SelectorCheckStatus.Broken,
                item.Selector, item.LocatorMethod, item.SourceFile, item.SourceLine,
                item.PageUrl, MatchCount: 0, Suggestion: null, Note: null)
            {
                Suggestions = new List<RepairSuggestion>
                {
                    new(replacement, "interactive", Confidence.High),
                },
            },
        };

        try
        {
            SelectorRewriter.Apply(single, backup, ct);
        }
        catch (Exception ex)
        {
            return new RepairOutcome(false, ex.Message);
        }

        var applied = single[0];
        return applied.Fixed
            ? new RepairOutcome(true, null)
            : new RepairOutcome(false, applied.FixError ?? "rewrite failed");
    }
}
