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
        _context = await s_fixture.NewContextAsync(ContextOptions).ConfigureAwait(false);
        _page = await _context.NewPageAsync().ConfigureAwait(false);

        _failureTracing = new FailureTracing();
        await _failureTracing.StartIfEnabledAsync(_context).ConfigureAwait(false);
    }

    [TestCleanup]
    public async Task MotusTestCleanup()
    {
        if (_context is not null)
        {
            var testFailed = TestContext.CurrentTestOutcome != UnitTestOutcome.Passed;
            if (_failureTracing is not null)
                await _failureTracing.StopAsync(_context, testFailed).ConfigureAwait(false);

            await _context.CloseAsync().ConfigureAwait(false);
            _context = null;
            _page = null;
        }
    }
}
