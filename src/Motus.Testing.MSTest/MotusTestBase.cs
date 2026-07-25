using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Motus.Abstractions;

namespace Motus.Testing.MSTest;

/// <summary>
/// Base class for MSTest browser tests. Shares a single browser across all tests
/// in the assembly and creates an isolated context per test.
/// Compatible with <c>[Parallelize]</c>.
/// </summary>
public abstract class MotusTestBase
{
    private static readonly BrowserFixture s_fixture = new();

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
    /// The browser context for the current test.
    /// </summary>
    protected IBrowserContext Context => _context ?? throw new InvalidOperationException(
        "Context not available. Ensure [TestInitialize] has run.");

    /// <summary>
    /// The page for the current test.
    /// </summary>
    protected IPage Page => _page ?? throw new InvalidOperationException(
        "Page not available. Ensure [TestInitialize] has run.");

    /// <summary>
    /// MSTest test context, used to detect test outcome for failure tracing.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Launches the shared browser. Apply <c>[AssemblyInitialize]</c> in your test assembly
    /// to call this method.
    /// </summary>
    public static async Task LaunchBrowserAsync(LaunchOptions? options = null)
    {
        await s_fixture.InitializeAsync(options).ConfigureAwait(false);
    }

    /// <summary>
    /// Disposes the shared browser. Apply <c>[AssemblyCleanup]</c> in your test assembly
    /// to call this method.
    /// </summary>
    public static async Task CloseBrowserAsync()
    {
        await s_fixture.DisposeAsync().ConfigureAwait(false);
    }

    [TestInitialize]
    public async Task MotusTestInitialize()
    {
        // If Chrome crashed during a previous test, the fixture auto-restarts.
        // Retry context+page creation to ride through the restart window.
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            IBrowserContext? context = null;
            try
            {
                context = await s_fixture.NewContextAsync(ContextOptions).ConfigureAwait(false);
                _page = await context.NewPageAsync().ConfigureAwait(false);
                _context = context;
                break;
            }
            catch
            {
                // A context created before the failure holds one of the fixture's slots, and the
                // fixture hands out a fixed few. Dropping it without closing it retires that slot
                // for the rest of the run, and a run that has retired them all waits here forever.
                if (context is not null)
                {
                    try { await s_fixture.CloseContextAsync(context).ConfigureAwait(false); }
                    catch { /* the browser it belonged to is already gone */ }
                }

                _context = null;
                _page = null;

                if (attempt >= maxAttempts)
                    throw;

                // Give the browser fixture time to restart Chrome.
                await Task.Delay(1000 * attempt).ConfigureAwait(false);
            }
        }

        _failureTracing = new FailureTracing();
        await _failureTracing.StartIfEnabledAsync(_context).ConfigureAwait(false);

        var testMethodName = TestContext?.TestName ?? TestMethodNameContext.Current;
        var methodInfo = testMethodName is not null
            ? GetType().GetMethod(testMethodName, BindingFlags.Public | BindingFlags.Instance)
            : null;
        var methodAttr = methodInfo?.GetCustomAttribute<PerformanceBudgetAttribute>();
        var classAttr = GetType().GetCustomAttribute<PerformanceBudgetAttribute>();
        var activeAttr = methodAttr ?? classAttr;
        var budget = activeAttr?.ToBudget();
        PerformanceBudgetContext.Push(budget);
        PerformanceBudgetContext.SetBudget(_page, budget);
    }

    [TestCleanup]
    public async Task MotusTestCleanup()
    {
        if (_page is not null)
            PerformanceBudgetContext.ClearBudget(_page);
        PerformanceBudgetContext.Clear();

        if (_context is not null)
        {
            try
            {
                var testFailed = TestContext?.CurrentTestOutcome != UnitTestOutcome.Passed;
                if (_failureTracing is not null)
                    await _failureTracing.StopAsync(_context, testFailed).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Browser may have crashed or disconnected; swallow so we don't
                // mask the original test failure with a cleanup exception.
            }

            try
            {
                // Always reached, even when tracing above failed: this is the only place the
                // context's slot goes back to the fixture, and a slot that never comes back is one
                // fewer test that can ever start.
                await s_fixture.CloseContextAsync(_context).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The context went with the browser it belonged to.
            }
            finally
            {
                _context = null;
                _page = null;
            }
        }
    }
}
