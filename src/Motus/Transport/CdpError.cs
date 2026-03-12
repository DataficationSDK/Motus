namespace Motus;

/// <summary>
/// Thrown when the browser returns a CDP protocol error in response to a command.
/// </summary>
internal sealed class CdpProtocolException : Exception
{
    internal int? Code { get; }

    internal CdpProtocolException(string message) : base(message) { }

    internal CdpProtocolException(int code, string message)
        : base(message)
    {
        Code = code;
    }
}

/// <summary>
/// Thrown when the CDP WebSocket connection is lost unexpectedly.
/// </summary>
internal sealed class CdpDisconnectedException : Exception
{
    internal CdpDisconnectedException()
        : base("CDP WebSocket disconnected.") { }

    internal CdpDisconnectedException(Exception innerException)
        : base("CDP WebSocket disconnected.", innerException) { }
}
