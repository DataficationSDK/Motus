namespace Motus;

/// <summary>
/// Thrown when the browser returns a BiDi protocol error in response to a command.
/// BiDi uses string error codes (e.g. "unknown error", "invalid argument") rather
/// than CDP's integer codes.
/// </summary>
internal sealed class BiDiProtocolException : Exception
{
    internal string? ErrorCode { get; }

    internal BiDiProtocolException(string message) : base(message) { }

    internal BiDiProtocolException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}

/// <summary>
/// Thrown when the BiDi WebSocket connection is lost unexpectedly.
/// </summary>
internal sealed class BiDiDisconnectedException : Exception
{
    internal BiDiDisconnectedException()
        : base("BiDi WebSocket disconnected.") { }

    internal BiDiDisconnectedException(Exception innerException)
        : base("BiDi WebSocket disconnected.", innerException) { }
}
