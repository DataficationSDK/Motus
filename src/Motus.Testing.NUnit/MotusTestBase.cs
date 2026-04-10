using System.Reflection;
using Motus.Abstractions;
using NUnit.Framework;

namespace Motus.Testing.NUnit;

/// <summary>
/// Base class for NUnit browser tests. Launches a browser per fixture
/// and creates an isolated context per test.
/// Compatible with <c>[Parallelizable(ParallelScope.All)]</c>.
/// </summary>
public abstract class MotusTestBase
{
    private readonly BrowserFixture _fixture = new();
    private IBrowserContext? _context;
    private IPage? _page;
    private FailureTracing? _failureTracing;

    /// <summary>
    /// Override to customize browser launch options.
    /// </summary>
    protected virtual LaunchOptions? LaunchOptions => null;

    /// <summary>
    /// Override to customize per-test context options.
    /// Default viewport is 1024x768.
    /// </summary>
    protected virtual ContextOptions? ContextOptions => new()
    {
        Viewport = new ViewportSize(1024, 768),
    };

    /// <summary>
    /// The browser instance for this test fixture.
    /// </summary>
    protected IBrowser Browser => _fixture.Browser;

    /// <summary>
    /// The browser context for the current test.
    /// </summary>
    protected IBrowserContext Context => _context ?? throw new InvalidOperationException(
        "Context not available. Ensure [SetUp] has run.");

    /// <summary>
    /// The page for the current test.
    /// </summary>
    protected IPage Page => _page ?? throw new InvalidOperationException(
        "Page not available. Ensure [SetUp] has run.");

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        await _fixture.InitializeAsync(LaunchOptions);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await _fixture.DisposeAsync();
    }

    [SetUp]
    public async Task SetUp()
    {
        _context = await _fixture.NewContextAsync(ContextOptions);
        _page = await _context.NewPageAsync();

        _failureTracing = new FailureTracing();
        await _failureTracing.StartIfEnabledAsync(_context);

        var methodInfo = TestContext.CurrentContext.Test.Method?.MethodInfo;
        var methodAttr = methodInfo?.GetCustomAttribute<PerformanceBudgetAttribute>();
        var classAttr = GetType().GetCustomAttribute<PerformanceBudgetAttribute>();
        var activeAttr = methodAttr ?? classAttr;
        var budget = activeAttr?.ToBudget();
        PerformanceBudgetContext.Push(budget);
        PerformanceBudgetContext.SetBudget(_page, budget);
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_page is not null)
            PerformanceBudgetContext.ClearBudget(_page);
        PerformanceBudgetContext.Clear();

        if (_context is not null)
        {
            var testFailed = TestContext.CurrentContext.Result.Outcome.Status == global::NUnit.Framework.Interfaces.TestStatus.Failed;
            if (_failureTracing is not null)
                await _failureTracing.StopAsync(_context, testFailed);

            await _context.CloseAsync();
            _context = null;
            _page = null;
        }
    }
}
