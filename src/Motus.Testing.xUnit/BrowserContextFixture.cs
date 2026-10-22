using System.Reflection;
using Motus.Abstractions;
using Xunit;

namespace Motus.Testing.xUnit;

/// <summary>
/// xUnit class fixture that creates an isolated browser context and page per test class.
/// Declare it with <c>IClassFixture&lt;BrowserContextFixture&gt;</c>; nothing else is needed.
/// The context comes from a browser shared by the whole test process, which launches when the
/// first fixture needs it and closes when the process exits.
/// Use <see cref="BrowserContextFixture{TBrowser}"/> instead to take the context from a
/// <see cref="SharedBrowserFixture"/> that a collection declared in the test project provides.
/// Note: Automatic failure tracing is not supported for xUnit class fixtures because
/// xUnit does not expose per-test outcome in <c>DisposeAsync</c>. Use
/// <c>Context.Tracing.StartAsync</c> / <c>StopAsync</c> manually for per-test traces.
/// </summary>
/// <remarks>
/// For a context per test rather than per class, create the fixture in the test class and call
/// <see cref="InitializeAsync"/> and <see cref="DisposeAsync"/> from the class's own
/// <see cref="IAsyncLifetime"/> methods. At most four contexts are open at once across the process;
/// a fixture that would exceed that waits in <see cref="InitializeAsync"/> for another to close.
/// </remarks>
public class BrowserContextFixture : IAsyncLifetime
{
    private readonly SharedBrowserFixture? _browserFixture;
    private IBrowserContext? _context;
    private IPage? _page;

    /// <summary>
    /// Creates a fixture whose context comes from the browser shared by the whole test process.
    /// </summary>
    public BrowserContextFixture()
    {
    }

    /// <summary>
    /// Creates a fixture whose context comes from the given shared browser. Made public by
    /// <see cref="BrowserContextFixture{TBrowser}"/>, because xUnit accepts only one public
    /// constructor on a class fixture.
    /// </summary>
    protected BrowserContextFixture(SharedBrowserFixture browserFixture)
    {
        ArgumentNullException.ThrowIfNull(browserFixture);
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
        IBrowserContext context;
        if (_browserFixture is null)
        {
            await DefaultBrowser.Fixture.EnsureInitializedAsync().ConfigureAwait(false);
            context = await DefaultBrowser.Fixture.NewContextAsync(ContextOptions).ConfigureAwait(false);
        }
        else
        {
            context = await _browserFixture.NewContextAsync(ContextOptions).ConfigureAwait(false);
        }

        try
        {
            _page = await context.NewPageAsync().ConfigureAwait(false);
        }
        catch
        {
            // The context is kept only once it has a page, so disposal would never see this one,
            // and it holds one of the few slots the browser fixtures hand out.
            try { await CloseContextAsync(context).ConfigureAwait(false); }
            catch { /* the browser it belonged to is already gone */ }
            throw;
        }

        _context = context;

        var testMethodName = TestMethodNameContext.Current;
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

    public async Task DisposeAsync()
    {
        if (_page is not null)
            PerformanceBudgetContext.ClearBudget(_page);
        PerformanceBudgetContext.Clear();

        var context = _context;
        _context = null;
        _page = null;

        // Closed through the fixture that created it rather than on its own: that is what returns
        // its slot, and once four slots are lost the next context waits forever.
        if (context is not null)
            await CloseContextAsync(context).ConfigureAwait(false);
    }

    private Task CloseContextAsync(IBrowserContext context)
        => _browserFixture is null
            ? DefaultBrowser.Fixture.CloseContextAsync(context)
            : _browserFixture.CloseContextAsync(context);
}
