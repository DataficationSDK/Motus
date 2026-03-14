using Motus.Runner.Services.Models;

namespace Motus.Runner.Services;

public interface ITestSessionService
{
    IReadOnlyList<DiscoveredTest> DiscoveredTests { get; }
    IReadOnlyDictionary<string, TestNodeState> States { get; }
    bool IsRunning { get; }
    string? RunningTestName { get; }
    string? FilterText { get; }
    event Action? StateChanged;

    Task LoadAssembliesAsync(string[] paths, string? filter);
    Task RunAllAsync(CancellationToken ct = default);
    Task RunTestAsync(string fullName, CancellationToken ct = default);
    Task RunClassAsync(string className, CancellationToken ct = default);
    void SetFilter(string? text);
    void RequestStop();
    void Reset();
}
