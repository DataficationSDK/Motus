using System.Collections.Concurrent;
using Motus;
using Motus.Runner.Services.Models;
using Motus.Runner.Services.Timeline;

namespace Motus.Runner.Services;

public sealed class TestSessionService : ITestSessionService
{
    private readonly TestDiscovery _discovery;
    private readonly TestExecutionService _executor;
    private readonly ITimelineService _timeline;
    private List<DiscoveredTest> _discoveredTests = [];
    private readonly ConcurrentDictionary<string, TestNodeState> _states = new();
    private HashSet<string> _lastRunTests = [];
    private CancellationTokenSource? _runCts;
    private int _running;

    public TestSessionService(TestDiscovery discovery, TestExecutionService executor, ITimelineService timeline)
    {
        _discovery = discovery;
        _executor = executor;
        _timeline = timeline;
    }

    public IReadOnlyList<DiscoveredTest> DiscoveredTests => _discoveredTests;
    public IReadOnlyDictionary<string, TestNodeState> States => _states;
    public bool IsRunning => _running != 0;
    public string? RunningTestName { get; private set; }
    public string? FilterText { get; private set; }
    public IReadOnlySet<string> LastRunTests => _lastRunTests;
    public ReporterCollection? Reporters { get; set; }
    public event Action? StateChanged;

    public Task LoadAssembliesAsync(string[] paths, string? filter)
    {
        var tests = _discovery.Discover(paths, filter);
        _discoveredTests = tests;
        _states.Clear();

        foreach (var test in tests)
        {
            _states[test.FullName] = test.IsIgnored
                ? new TestNodeState(test.FullName, TestStatus.Skipped, null, test.IgnoreReason ?? "Ignored", null)
                : new TestNodeState(test.FullName, TestStatus.Pending, null, null, null);
        }

        NotifyStateChanged();
        return Task.CompletedTask;
    }

    public async Task RunAllAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            _timeline.Clear();
            ResetAllToPending();
            _lastRunTests = new HashSet<string>(_discoveredTests.Select(t => t.FullName));
            _timeline.CurrentTestName = "[Assembly Setup]";
            NotifyStateChanged();

            await _executor.ExecuteAsync(_discoveredTests, UpdateTestState, _runCts.Token, Reporters);
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            Interlocked.Exchange(ref _running, 0);
            NotifyStateChanged();
        }
    }

    public async Task RunTestAsync(string fullName, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            var test = _discoveredTests.Find(t => t.FullName == fullName);
            if (test is null) return;

            _timeline.Clear();
            _lastRunTests = [fullName];
            _states[fullName] = new TestNodeState(fullName, TestStatus.Pending, null, null, null);
            _timeline.CurrentTestName = "[Assembly Setup]";
            NotifyStateChanged();

            await _executor.ExecuteAsync([test], UpdateTestState, _runCts.Token, Reporters);
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            Interlocked.Exchange(ref _running, 0);
            NotifyStateChanged();
        }
    }

    public async Task RunClassAsync(string className, CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return;

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            _timeline.Clear();
            var tests = _discoveredTests.Where(t => t.TestClass.FullName == className).ToList();
            _lastRunTests = new HashSet<string>(tests.Select(t => t.FullName));
            foreach (var test in tests)
            {
                _states[test.FullName] = new TestNodeState(test.FullName, TestStatus.Pending, null, null, null);
            }
            _timeline.CurrentTestName = "[Assembly Setup]";
            NotifyStateChanged();

            await _executor.ExecuteAsync(tests, UpdateTestState, _runCts.Token, Reporters);
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            Interlocked.Exchange(ref _running, 0);
            NotifyStateChanged();
        }
    }

    public void SetFilter(string? text)
    {
        FilterText = string.IsNullOrWhiteSpace(text) ? null : text;
        NotifyStateChanged();
    }

    public void RequestStop()
    {
        _runCts?.Cancel();
    }

    public void Reset()
    {
        if (IsRunning) return;

        ResetAllToPending();
        _lastRunTests = [];
        RunningTestName = null;
        _timeline.CurrentTestName = null;
        _timeline.Clear();
        NotifyStateChanged();
    }

    private void ResetAllToPending()
    {
        foreach (var test in _discoveredTests)
        {
            _states[test.FullName] = test.IsIgnored
                ? new TestNodeState(test.FullName, TestStatus.Skipped, null, test.IgnoreReason ?? "Ignored", null)
                : new TestNodeState(test.FullName, TestStatus.Pending, null, null, null);
        }
    }

    private void UpdateTestState(TestNodeState state)
    {
        _states[state.FullName] = state;

        if (state.Status == TestStatus.Running)
        {
            // Extract just the method name for display (last segment of FullName)
            var parts = state.FullName.Split('.');
            RunningTestName = parts.Length > 0 ? parts[^1] : state.FullName;
            _timeline.CurrentTestName = state.FullName;
        }
        else if (state.Status is TestStatus.Passed or TestStatus.Failed or TestStatus.Skipped)
        {
            RunningTestName = null;
            _timeline.CurrentTestName = "[Assembly Setup]";
        }

        NotifyStateChanged();
    }

    private void NotifyStateChanged() => StateChanged?.Invoke();
}
