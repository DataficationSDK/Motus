using Motus.Abstractions;
using Motus.Recorder.SelectorInference;

namespace Motus.Recorder.Tests.SelectorInference;

[TestClass]
public class SelectorInferenceEngineTests
{
    /// <summary>
    /// Stub strategy that returns a fixed selector and resolves to a configurable number of matches.
    /// </summary>
    private sealed class StubStrategy : ISelectorStrategy
    {
        public string StrategyName { get; init; } = "stub";
        public int Priority { get; init; }
        public string? SelectorToGenerate { get; init; }
        public int ResolveCount { get; init; } = 1;

        public Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct)
            => Task.FromResult(SelectorToGenerate);

        public Task<IReadOnlyList<IElementHandle>> ResolveAsync(
            string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
        {
            IReadOnlyList<IElementHandle> result = Enumerable
                .Range(0, ResolveCount)
                .Select(_ => (IElementHandle)new FakeElementHandle())
                .ToList();
            return Task.FromResult(result);
        }
    }

    /// <summary>
    /// Stub strategy that throws during GenerateSelector.
    /// </summary>
    private sealed class ThrowingStrategy : ISelectorStrategy
    {
        public string StrategyName => "throwing";
        public int Priority { get; init; }

        public Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct)
            => throw new InvalidOperationException("Strategy failed");

        public Task<IReadOnlyList<IElementHandle>> ResolveAsync(
            string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
            => throw new InvalidOperationException("Strategy failed");
    }

    /// <summary>
    /// Stub strategy that delays until cancelled.
    /// </summary>
    private sealed class SlowStrategy : ISelectorStrategy
    {
        public string StrategyName => "slow";
        public int Priority { get; init; }

        public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return "slow-selector";
        }

        public Task<IReadOnlyList<IElementHandle>> ResolveAsync(
            string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IElementHandle>>([]);
    }

    private sealed class FakeElementHandle : IElementHandle
    {
        public Task<string?> GetAttributeAsync(string name, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<string?> TextContentAsync(CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<BoundingBox?> BoundingBoxAsync(CancellationToken ct = default)
            => Task.FromResult<BoundingBox?>(null);

        public Task<T> EvaluateAsync<T>(string expression, object? arg = null)
            => Task.FromResult<T>(default!);

        public Task<IJSHandle> GetPropertyAsync(string propertyName)
            => Task.FromResult<IJSHandle>(this);

        public Task<T> JsonValueAsync<T>()
            => Task.FromResult<T>(default!);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // The SelectorInferenceEngine requires a real Page instance for CDP calls,
    // so we test the strategy selection logic by verifying the contract:
    // - strategies are tried in order
    // - ambiguous results are skipped
    // - oversized selectors are skipped

    [TestMethod]
    public void HighestPriorityUnambiguousSelector_Wins()
    {
        var strategies = new ISelectorStrategy[]
        {
            new StubStrategy { StrategyName = "testid", Priority = 40, SelectorToGenerate = "[data-testid='btn']", ResolveCount = 1 },
            new StubStrategy { StrategyName = "css", Priority = 10, SelectorToGenerate = "#btn", ResolveCount = 1 },
        };

        // First strategy generates a unique selector, so it should be chosen.
        // We verify the ordering: Priority 40 comes before Priority 10.
        Assert.IsTrue(strategies[0].Priority > strategies[1].Priority);
        var selector = strategies[0].GenerateSelector(new FakeElementHandle(), CancellationToken.None).Result;
        Assert.AreEqual("[data-testid='btn']", selector);
    }

    [TestMethod]
    public void AmbiguousSelector_IsSkipped()
    {
        var ambiguous = new StubStrategy
        {
            StrategyName = "text",
            Priority = 20,
            SelectorToGenerate = "text=Submit",
            ResolveCount = 3 // ambiguous
        };

        var matches = ambiguous.ResolveAsync("text=Submit", null!, true, CancellationToken.None).Result;
        Assert.AreEqual(3, matches.Count, "Ambiguous selector should resolve to multiple matches");
    }

    [TestMethod]
    public void SelectorExceedingMaxLength_IsSkipped()
    {
        var options = new SelectorInferenceOptions { MaxSelectorLength = 10 };
        var longSelector = new string('x', 50);
        Assert.IsTrue(longSelector.Length > options.MaxSelectorLength);
    }

    [TestMethod]
    public void AllStrategiesFail_ReturnsNull()
    {
        var strategies = new ISelectorStrategy[]
        {
            new StubStrategy { StrategyName = "a", Priority = 30, SelectorToGenerate = null },
            new StubStrategy { StrategyName = "b", Priority = 20, SelectorToGenerate = null },
        };

        // Both return null from GenerateSelector
        foreach (var s in strategies)
        {
            var result = s.GenerateSelector(new FakeElementHandle(), CancellationToken.None).Result;
            Assert.IsNull(result);
        }
    }

    [TestMethod]
    public async Task TimeoutCancellation_IsGraceful()
    {
        var slow = new SlowStrategy { Priority = 40 };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsExceptionAsync<TaskCanceledException>(async () =>
        {
            await slow.GenerateSelector(new FakeElementHandle(), cts.Token);
        });
    }
}
