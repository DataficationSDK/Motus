using Motus.Abstractions;
using InternalPage = Motus.Page;

namespace Motus.Runner.Services.SelectorRepair;

internal sealed class SelectorRepairService : ISelectorRepairService
{
    private readonly RunnerOptions _options;
    private int _currentIndex = -1;
    private RepairOutcome? _currentOutcome;

    public SelectorRepairService(RunnerOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<RepairQueueItem> Items => SelectorRepairBridge.Items;

    public int CurrentIndex => _currentIndex;

    public RepairQueueItem? CurrentItem =>
        _currentIndex >= 0 && _currentIndex < Items.Count ? Items[_currentIndex] : null;

    public RepairOutcome? CurrentOutcome => _currentOutcome;

    public bool IsComplete => _currentIndex >= Items.Count;

    public int Accepted { get; private set; }
    public int Skipped { get; private set; }
    public int Failed { get; private set; }

    public event Action? StateChanged;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (!_options.RepairMode)
            return;

        if (_currentIndex >= 0)
            return;

        _currentIndex = 0;
        await NavigateAndHighlightAsync(ct).ConfigureAwait(false);
        Notify();
    }

    public async Task AcceptAsync(string replacement, CancellationToken ct = default)
    {
        var item = CurrentItem;
        if (item is null)
            return;

        var apply = SelectorRepairBridge.ApplyDecision;
        if (apply is null)
        {
            _currentOutcome = new RepairOutcome(false, "internal: no apply delegate");
            Failed++;
            Notify();
            return;
        }

        RepairOutcome outcome;
        try
        {
            outcome = apply(item, replacement);
        }
        catch (Exception ex)
        {
            outcome = new RepairOutcome(false, ex.Message);
        }

        _currentOutcome = outcome;
        if (outcome.Fixed)
            Accepted++;
        else
            Failed++;

        Notify();

        if (outcome.Fixed)
            await AdvanceAsync(ct).ConfigureAwait(false);
    }

    public async Task SkipAsync(CancellationToken ct = default)
    {
        if (CurrentItem is null)
            return;

        Skipped++;
        _currentOutcome = null;
        await AdvanceAsync(ct).ConfigureAwait(false);
    }

    public Task FinishAsync()
    {
        var summary = new RepairSummary(Accepted, Skipped, Failed);
        SelectorRepairBridge.Complete(summary);
        return Task.CompletedTask;
    }

    private async Task AdvanceAsync(CancellationToken ct)
    {
        _currentIndex++;
        _currentOutcome = null;

        if (_currentIndex >= Items.Count)
        {
            await FinishAsync().ConfigureAwait(false);
            Notify();
            return;
        }

        await NavigateAndHighlightAsync(ct).ConfigureAwait(false);
        Notify();
    }

    private async Task NavigateAndHighlightAsync(CancellationToken ct)
    {
        var item = CurrentItem;
        var page = SelectorRepairBridge.Page;
        if (item is null || page is null)
            return;

        try
        {
            await page.GotoAsync(item.PageUrl, new NavigationOptions { WaitUntil = WaitUntil.Load })
                .ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: navigation may fail offline / for data: URLs already on screen.
        }

        if (item.HighlightSelector is not null)
        {
            try
            {
                await HighlightHelper.HighlightAsync(page, item.HighlightSelector, ct).ConfigureAwait(false);
            }
            catch
            {
                // Highlight is a visual aid; never block the workflow.
            }
        }
    }

    private void Notify() => StateChanged?.Invoke();
}
