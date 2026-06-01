using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Motus.Mcp;

namespace Motus.Mcp.Http;

/// <summary>
/// Hosts the Motus MCP server over Streamable HTTP for concurrent remote clients. Each connected
/// client (MCP session) gets its own isolated browser, contexts, and tabs; the tool set is the same
/// one the stdio host serves.
/// </summary>
public static class McpHttpServerHost
{
    /// <summary>
    /// Builds and runs the HTTP host until the token is cancelled or the process is stopped.
    /// </summary>
    public static async Task StartAsync(McpHttpServerOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var (app, registry) = Build(options);
        try
        {
            await app.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await registry.DisposeAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the web application and its session registry without running it. The registry is
    /// returned so the caller can dispose it on shutdown (the host registers it as an external
    /// instance, which the DI container does not own). Exposed for tests that drive the host on a
    /// chosen port and read back the bound address.
    /// </summary>
    internal static (WebApplication App, McpSessionRegistry Registry) Build(McpHttpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!IsLoopback(options.Host) && string.IsNullOrEmpty(options.Token))
        {
            throw new InvalidOperationException(
                $"Refusing to bind non-loopback host '{options.Host}' without an authentication token. "
                + "Pass a token or bind a loopback address (127.0.0.1).");
        }

        var builder = WebApplication.CreateBuilder();

        // Keep the web host quiet on stdout; only warnings and errors surface unless the operator
        // raises the level. There is no JSON-RPC-over-stdout concern here (that is the stdio host),
        // but a chatty server is noise in a CLI session.
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.WebHost.UseUrls($"http://{options.Host}:{options.Port}");

        // The registry is created here so the per-session handler (a closure) and the per-session
        // tool service factories can share it. It is registered as an external instance and disposed
        // by the host, not the container.
        var registry = new McpSessionRegistry();
        builder.Services.AddSingleton(registry);

        // The tools resolve these four services by type. Each call returns the bundle for the
        // session the call belongs to, so concurrent clients never see each other's browser. The
        // bundle owns the instances' lifetimes, so none of these is disposed by the per-call scope.
        builder.Services.AddTransient(_ => registry.RequireCurrent().Pages);
        builder.Services.AddTransient(_ => registry.RequireCurrent().Dialogs);
        builder.Services.AddTransient(_ => registry.RequireCurrent().Console);
        builder.Services.AddTransient(_ => registry.RequireCurrent().Network);

        var launchOptions = options.LaunchOptions;

        var mcpBuilder = builder.Services
            .AddMcpServer(McpServerConfiguration.ConfigureServerOptions)
            .WithHttpTransport(transport =>
            {
                transport.IdleTimeout = options.IdleTimeout;

                // Run every tool call of a session on the session's execution context, so the
                // ambient bundle set below flows into the tool service factories.
                transport.PerSessionExecutionContext = true;

                // One bundle per session: created when the session starts, made current on this
                // execution context, and disposed (browser torn down) when the session ends.
                transport.RunSessionHandler = async (httpContext, server, sessionToken) =>
                {
                    var sessionId = server.SessionId
                        ?? throw new InvalidOperationException("A stateful HTTP session has no session id.");
                    var loggerFactory = httpContext.RequestServices.GetService<ILoggerFactory>();
                    var bundle = new McpSessionBundle(launchOptions, loggerFactory);
                    registry.Register(sessionId, bundle);
                    try
                    {
                        await server.RunAsync(sessionToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        await registry.RemoveAsync(sessionId).ConfigureAwait(false);
                    }
                };
            });
        mcpBuilder.AddMotusTools();

        var app = builder.Build();

        // Gate the endpoint on the bearer token when one is configured. Done as terminal middleware
        // rather than a full authentication scheme to keep the dependency surface minimal for v1.
        if (!string.IsNullOrEmpty(options.Token))
        {
            var expected = Encoding.UTF8.GetBytes($"Bearer {options.Token}");
            app.Use(async (context, next) =>
            {
                var presented = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
                if (!CryptographicOperations.FixedTimeEquals(presented, expected))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = "Bearer";
                    return;
                }

                await next().ConfigureAwait(false);
            });
        }

        app.MapMcp();

        return (app, registry);
    }

    /// <summary>
    /// Starts the host on its configured (or OS-assigned, when the port is 0) address and returns a
    /// handle exposing the bound base address and the session registry. For tests that drive the
    /// running server with a real MCP client.
    /// </summary>
    internal static async Task<RunningServer> StartForTestingAsync(
        McpHttpServerOptions options,
        CancellationToken cancellationToken = default)
    {
        var (app, registry) = Build(options);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        var baseAddress = new Uri(app.Urls.First(), UriKind.Absolute);
        return new RunningServer(app, registry, baseAddress);
    }

    /// <summary>A running HTTP host handle for tests: the bound address, the registry, and teardown.</summary>
    internal sealed class RunningServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        internal RunningServer(WebApplication app, McpSessionRegistry registry, Uri baseAddress)
        {
            _app = app;
            Registry = registry;
            BaseAddress = baseAddress;
        }

        /// <summary>The base address the server is listening on.</summary>
        public Uri BaseAddress { get; }

        /// <summary>The live session registry, so tests can assert per-session lifecycle.</summary>
        public McpSessionRegistry Registry { get; }

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync().ConfigureAwait(false);
            await Registry.DisposeAsync().ConfigureAwait(false);
            await _app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool IsLoopback(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
