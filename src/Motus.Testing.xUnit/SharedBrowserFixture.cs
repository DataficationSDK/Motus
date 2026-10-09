using Motus.Abstractions;
using Xunit;

namespace Motus.Testing.xUnit;

/// <summary>
/// xUnit collection fixture that manages a browser shared by the test classes of one collection.
/// Declare the collection in the test project, since xUnit reads collection definitions only from
/// the test assembly: <c>[CollectionDefinition("Name")] public class NameCollection : MotusCollection { }</c>.
/// Classes in the collection take their context from it with
/// <see cref="BrowserContextFixture{TBrowser}"/>.
/// </summary>
/// <remarks>
/// Subclass it and override <see cref="LaunchOptions"/> to give a collection a browser launched with
/// its own options; the collection then names the subclass in <c>ICollectionFixture&lt;T&gt;</c>,
/// and its classes use <c>BrowserContextFixture&lt;TSubclass&gt;</c>.
/// </remarks>
public class SharedBrowserFixture : IAsyncLifetime
{
    private readonly BrowserFixture _fixture = new();

    /// <summary>
    /// Override to customize browser launch options.
    /// </summary>
    protected virtual LaunchOptions? LaunchOptions => null;

    /// <summary>
    /// The shared browser instance.
    /// </summary>
    public IBrowser Browser => _fixture.Browser;

    /// <summary>
    /// Creates a new isolated browser context, launching the browser first if it has not been.
    /// Close it with <see cref="CloseContextAsync"/>, which also frees its place among the
    /// contexts allowed open at once.
    /// </summary>
    public async Task<IBrowserContext> NewContextAsync(ContextOptions? options = null)
    {
        await _fixture.EnsureInitializedAsync(LaunchOptions).ConfigureAwait(false);
        return await _fixture.NewContextAsync(options).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes a context created by <see cref="NewContextAsync"/> and frees its place among the
    /// contexts allowed open at once, even when the close fails because the browser has gone.
    /// </summary>
    public Task CloseContextAsync(IBrowserContext context)
        => _fixture.CloseContextAsync(context);

    public async Task InitializeAsync()
    {
        await _fixture.EnsureInitializedAsync(LaunchOptions);
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
