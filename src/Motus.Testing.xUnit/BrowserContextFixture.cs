using Motus.Abstractions;
using Xunit;

namespace Motus.Testing.xUnit;

/// <summary>
/// xUnit class fixture that creates an isolated browser context and page per test class.
/// Requires <see cref="SharedBrowserFixture"/> via collection fixture injection.
/// Note: Automatic failure tracing is not supported for xUnit class fixtures because
/// xUnit does not expose per-test outcome in <c>DisposeAsync</c>. Use
/// <c>Context.Tracing.StartAsync</c> / <c>StopAsync</c> manually for per-test traces.
/// </summary>
public class BrowserContextFixture : IAsyncLifetime
{
    private readonly SharedBrowserFixture _browserFixture;
    private IBrowserContext? _context;
    private IPage? _page;

    public BrowserContextFixture(SharedBrowserFixture browserFixture)
    {
        _browserFixture = browserFixture;
    }

    /// <summary>
    /// Override to customize per-class context options.
    /// </summary>
    protected virtual ContextOptions? ContextOptions => null;

    /// <summary>
    /// The browser context for this test class.
    /// </summary>
    public IBrowserContext Context => _context ?? throw new InvalidOperationException(
        "Context not available. Ensure InitializeAsync has run.");

    /// <summary>
    /// A page within the test class context.
    /// </summary>
    public IPage Page => _page ?? throw new InvalidOperationException(
        "Page not available. Ensure InitializeAsync has run.");

    public async Task InitializeAsync()
    {
        _context = await _browserFixture.NewContextAsync(ContextOptions);
        _page = await _context.NewPageAsync();
    }

    public async Task DisposeAsync()
    {
        if (_context is not null)
        {
            await _context.CloseAsync();
            _context = null;
            _page = null;
        }
    }
}
