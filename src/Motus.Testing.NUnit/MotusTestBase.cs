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
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                _context = await _fixture.NewContextAsync(ContextOptions);
                _page = await _context.NewPageAsync();
                break;
            }
            catch when (attempt < maxAttempts)
            {
                _context = null;
                _page = null;
                await Task.Delay(1000 * attempt);
            }
        }

        var testMethodName = TestMethodNameContext.Current;

        // NUnit's test name carries the arguments of a parameterized case, which keeps the
        // artifacts of one case apart from those of the next.
        var testName = testMethodName ?? TestContext.CurrentContext.Test.Name;
        _failureTracing = new FailureTracing(
            string.IsNullOrEmpty(testName) ? GetType().Name : $"{GetType().Name}.{testName}");
        await _failureTracing.StartIfEnabledAsync(_context);

        var methodInfo = testMethodName is not null
            ? GetType().GetMethod(testMethodName, BindingFlags.Public | BindingFlags.Instance)
            : TestContext.CurrentContext.Test.Method?.MethodInfo;
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
            try
            {
                var testFailed = TestContext.CurrentContext.Result.Outcome.Status == global::NUnit.Framework.Interfaces.TestStatus.Failed;
                if (_failureTracing is not null)
                {
                    await _failureTracing.StopAsync(_context, testFailed);
                    ReportFailureArtifacts(_failureTracing);
                }

                await _fixture.CloseContextAsync(_context);
            }
            catch (Exception) when (_context is not null)
            {
                // Browser may have crashed or disconnected; swallow so we don't
                // mask the original test failure with a cleanup exception.
            }
            finally
            {
                _context = null;
                _page = null;
            }
        }
    }

    /// <summary>
    /// Attaches what was saved for a failed test to its result and says where it went, so the
    /// trace is one click away from the test's entry in the results.
    /// </summary>
    private static void ReportFailureArtifacts(FailureTracing tracing)
    {
        if (tracing.TracePath is not null)
            ReportFailureArtifact(tracing.TracePath, "trace");

        foreach (var path in tracing.ScreenshotPaths)
            ReportFailureArtifact(path, "screenshot");
    }

    private static void ReportFailureArtifact(string path, string kind)
    {
        try
        {
            TestContext.AddTestAttachment(path, $"Motus failure {kind}");
        }
        catch (Exception)
        {
            // The file is still on disk; failing to attach it must not mask the test's own failure.
        }

        try
        {
            TestContext.Out.WriteLine($"Motus: failure {kind} saved to {path}");
        }
        catch (Exception)
        {
            // As above: reporting is a courtesy, never a second failure.
        }
    }
}
