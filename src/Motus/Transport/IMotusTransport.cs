namespace Motus;

/// <summary>
/// Lifecycle contract for a protocol transport connection.
/// Carries the disconnection event, capability flags, and clean disposal.
/// </summary>
internal interface IMotusTransport : IAsyncDisposable
{
    /// <summary>
    /// Raised when the underlying connection is lost. The exception (if any) is provided.
    /// </summary>
    event Action<Exception?>? Disconnected;

    /// <summary>
    /// The set of capabilities supported by this transport.
    /// </summary>
    MotusCapabilities Capabilities { get; }
}
