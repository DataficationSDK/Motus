using Motus.Abstractions;

namespace Motus.Runner.Services.SelectorRepair;

/// <summary>
/// Cross-process seam between the CLI host (which owns the browser, the source files,
/// and the SelectorRewriter) and the in-process Motus.Runner Blazor UI. The CLI calls
/// <see cref="Begin"/> before starting the runner; the runner reads <see cref="Items"/>,
/// <see cref="Page"/>, and invokes <see cref="ApplyDecision"/> per accepted suggestion.
/// </summary>
public static class SelectorRepairBridge
{
    public static IReadOnlyList<RepairQueueItem> Items { get; private set; } = [];

    public static IPage? Page { get; private set; }

    public static Func<RepairQueueItem, string, RepairOutcome>? ApplyDecision { get; private set; }

    public static TaskCompletionSource<RepairSummary>? Completion { get; private set; }

    public static void Begin(
        IReadOnlyList<RepairQueueItem> items,
        IPage? page,
        Func<RepairQueueItem, string, RepairOutcome> applyDecision)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        Page = page;
        ApplyDecision = applyDecision ?? throw new ArgumentNullException(nameof(applyDecision));
        Completion = new TaskCompletionSource<RepairSummary>(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static void Complete(RepairSummary summary)
    {
        Completion?.TrySetResult(summary);
    }

    public static void Reset()
    {
        Items = [];
        Page = null;
        ApplyDecision = null;
        Completion = null;
    }
}
