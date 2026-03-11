namespace Motus.Abstractions;

/// <summary>
/// Simple logging interface for Motus plugins. Keeps the abstractions package dependency-free.
/// </summary>
public interface IMotusLogger
{
    /// <summary>
    /// Logs an informational message.
    /// </summary>
    /// <param name="message">The message to log.</param>
    void Log(string message);

    /// <summary>
    /// Logs a warning message.
    /// </summary>
    /// <param name="message">The warning message.</param>
    void LogWarning(string message);

    /// <summary>
    /// Logs an error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="exception">An optional exception associated with the error.</param>
    void LogError(string message, Exception? exception = null);
}
