using System.Collections.Concurrent;

namespace Motus.Mcp;

/// <summary>
/// Tracks the live <see cref="McpSessionBundle"/> for each connected client and exposes the one that
/// belongs to the call currently executing. Registered as a singleton in the HTTP host; the
/// per-session tool services resolve through <see cref="Current"/>.
/// </summary>
/// <remarks>
/// The MCP SDK has no per-session dependency-injection scope: with the default settings a new scope
/// is created per tool call, and disabling that resolves everything from the root provider, shared
/// across every client. So per-session state is keyed by the session id here instead. The host sets
/// <see cref="Current"/> once per session (on the session's execution context, which the
/// per-session execution-context option flows into every tool call), and the tool service factories
/// read it. The forward map by id lets the host find and dispose a session's bundle when it ends.
/// </remarks>
public sealed class McpSessionRegistry : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, McpSessionBundle> _bundles = new(StringComparer.Ordinal);
    private readonly AsyncLocal<McpSessionBundle?> _current = new();
    private int _disposed;

    /// <summary>
    /// The bundle for the session the current call belongs to, or null on an execution context that
    /// has no session (for example before any session is established).
    /// </summary>
    public McpSessionBundle? Current => _current.Value;

    /// <summary>The number of live sessions. Exposed for tests and diagnostics.</summary>
    public int Count => _bundles.Count;

    /// <summary>
    /// Returns the current session's bundle, or throws if none is set. Tool service factories call
    /// this so a misconfigured host fails loudly rather than acting on a shared or missing session.
    /// </summary>
    public McpSessionBundle RequireCurrent()
        => _current.Value ?? throw new InvalidOperationException(
            "No MCP session is active on this execution context. The per-session services must be "
            + "resolved within a session whose bundle has been registered.");

    /// <summary>
    /// Registers a bundle for a session and makes it current on this execution context. Called by
    /// the host's session handler before it runs the session.
    /// </summary>
    public void Register(string sessionId, McpSessionBundle bundle)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);
        ArgumentNullException.ThrowIfNull(bundle);

        _bundles[sessionId] = bundle;
        _current.Value = bundle;
    }

    /// <summary>Removes a session's bundle and disposes it. Safe to call for an unknown id.</summary>
    public async ValueTask RemoveAsync(string sessionId)
    {
        ArgumentException.ThrowIfNullOrEmpty(sessionId);

        if (_bundles.TryRemove(sessionId, out var bundle))
        {
            await bundle.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Disposes every remaining session. Called on host shutdown.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        foreach (var sessionId in _bundles.Keys)
        {
            if (_bundles.TryRemove(sessionId, out var bundle))
            {
                await bundle.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
