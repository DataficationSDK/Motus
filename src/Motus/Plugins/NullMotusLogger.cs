using Motus.Abstractions;

namespace Motus;

internal sealed class NullMotusLogger : IMotusLogger
{
    internal static readonly NullMotusLogger Instance = new();

    private NullMotusLogger() { }

    public void Log(string message) { }
    public void LogWarning(string message) { }
    public void LogError(string message, Exception? exception = null) { }
}
