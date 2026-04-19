namespace Motus.Runner.Services.SelectorRepair;

public interface ISelectorRepairService
{
    IReadOnlyList<RepairQueueItem> Items { get; }
    int CurrentIndex { get; }
    RepairQueueItem? CurrentItem { get; }
    RepairOutcome? CurrentOutcome { get; }
    bool IsComplete { get; }
    int Accepted { get; }
    int Skipped { get; }
    int Failed { get; }

    event Action? StateChanged;

    Task InitializeAsync(CancellationToken ct = default);
    Task AcceptAsync(string replacement, CancellationToken ct = default);
    Task SkipAsync(CancellationToken ct = default);
    Task FinishAsync();
}
