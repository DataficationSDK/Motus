using Motus.Abstractions;
using Xunit;

namespace Motus.Testing.xUnit;

/// <summary>
/// xUnit collection fixture that manages a shared browser instance.
/// Use with <see cref="MotusCollection"/> to share a single browser across test classes.
/// </summary>
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
    /// Creates a new isolated browser context.
    /// </summary>
    public Task<IBrowserContext> NewContextAsync(ContextOptions? options = null)
        => _fixture.NewContextAsync(options);

    public async Task InitializeAsync()
    {
        await _fixture.InitializeAsync(LaunchOptions);
    }

    public async Task DisposeAsync()
    {
        await _fixture.DisposeAsync();
    }
}
