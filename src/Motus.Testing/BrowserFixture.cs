using Motus.Abstractions;

namespace Motus.Testing;

/// <summary>
/// Manages a single browser instance for use in test fixtures.
/// Call <see cref="InitializeAsync"/> once (typically in assembly/collection setup),
/// then create isolated contexts per test via <see cref="NewContextAsync"/>.
/// </summary>
public sealed class BrowserFixture : IAsyncDisposable
{
    private IBrowser? _browser;

    /// <summary>
    /// Launches a browser instance with the given options.
    /// </summary>
    public async Task InitializeAsync(LaunchOptions? options = null)
    {
        _browser = await MotusLauncher.LaunchAsync(options);
    }

    /// <summary>
    /// The launched browser instance.
    /// </summary>
    public IBrowser Browser => _browser ?? throw new InvalidOperationException(
        "Browser not initialized. Call InitializeAsync first.");

    /// <summary>
    /// Creates a new isolated browser context.
    /// </summary>
    public async Task<IBrowserContext> NewContextAsync(ContextOptions? options = null)
        => await Browser.NewContextAsync(options);

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
            _browser = null;
        }
    }
}
