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

    /// <summary>
    /// Override to customize browser launch options.
    /// </summary>
    protected virtual LaunchOptions? LaunchOptions => null;

    /// <summary>
    /// Override to customize per-test context options.
    /// </summary>
    protected virtual ContextOptions? ContextOptions => null;

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
    /// Launches the shared browser. Apply <c>[AssemblyInitialize]</c> in your test assembly
    /// to call this method.
    /// </summary>
    public static async Task LaunchBrowserAsync(LaunchOptions? options = null)
    {
        await s_fixture.InitializeAsync(options);
    }

    /// <summary>
    /// Disposes the shared browser. Apply <c>[AssemblyCleanup]</c> in your test assembly
    /// to call this method.
    /// </summary>
    public static async Task CloseBrowserAsync()
    {
        await s_fixture.DisposeAsync();
    }

    [TestInitialize]
    public async Task MotusTestInitialize()
    {
        _context = await s_fixture.NewContextAsync(ContextOptions);
        _page = await _context.NewPageAsync();
    }

    [TestCleanup]
    public async Task MotusTestCleanup()
    {
        if (_context is not null)
        {
            await _context.CloseAsync();
            _context = null;
            _page = null;
        }
    }
}
