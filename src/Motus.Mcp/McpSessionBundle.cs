using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// The full per-client object graph the MCP tools act on: the browser session manager, the
/// page-following observers, and the active-page service that ties them together. One bundle
/// exists per connected client so that concurrent clients (over HTTP) get fully isolated browsers,
/// contexts, and tabs.
/// </summary>
/// <remarks>
/// This is the exact graph the stdio host builds inline; pulling it into one type lets the HTTP
/// host create and tear down one instance per session. The bundle owns the lifetimes: disposing it
/// shuts the active-page service down and tears the browser down through
/// <see cref="BrowserSessionManager"/>.
/// </remarks>
public sealed class McpSessionBundle : IAsyncDisposable
{
    private int _disposed;

    public McpSessionBundle(McpServerLaunchOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        loggerFactory ??= NullLoggerFactory.Instance;

        Sessions = new BrowserSessionManager(options, loggerFactory.CreateLogger<BrowserSessionManager>());
        Dialogs = new DialogService();
        Console = new ConsoleService();
        Network = new NetworkService();
        Pages = new ActivePageService(Sessions, Dialogs, Console, Network);
    }

    /// <summary>Owns the browser and its named, isolated contexts.</summary>
    public BrowserSessionManager Sessions { get; }

    /// <summary>Captures the active page's dialog events.</summary>
    public DialogService Dialogs { get; }

    /// <summary>Buffers the active page's console and page-error messages.</summary>
    public ConsoleService Console { get; }

    /// <summary>Holds route mocks and the active page's request log.</summary>
    public NetworkService Network { get; }

    /// <summary>Resolves the active page and keeps per-page snapshot refs alive between calls.</summary>
    public ActivePageService Pages { get; }

    /// <summary>
    /// Tears the session down: shuts the page service and disposes the browser. Safe to call more
    /// than once.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Pages.Shutdown();
        await Sessions.DisposeAsync().ConfigureAwait(false);
    }
}
