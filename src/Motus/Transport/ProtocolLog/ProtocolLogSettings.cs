using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Decides whether a launch or connect logs its protocol traffic, and where to.
/// </summary>
/// <remarks>
/// The environment is read on every launch and connect rather than once per process like the other
/// <c>MOTUS_*</c> variables, so a long-lived host picks a change up the next time it connects.
/// </remarks>
internal static class ProtocolLogSettings
{
    /// <summary>The environment variable that turns the log on without a code change.</summary>
    internal const string EnvironmentVariable = "MOTUS_PROTOCOL_LOG";

    /// <summary>The destination that names the standard error stream rather than a file.</summary>
    internal const string StandardError = "stderr";

    private const string StandardOutput = "stdout";

    /// <summary>
    /// Resolves the options a caller passed against the environment.
    /// </summary>
    /// <returns>
    /// The options to log with, carrying a usable destination, or <see langword="null"/> when the
    /// log is off.
    /// </returns>
    /// <remarks>
    /// Options set in code win. When they name neither a path nor a sink, the destination comes from
    /// the environment and every other setting from the code, so a test can keep its own limits and
    /// still be switched on from outside.
    /// </remarks>
    internal static ProtocolLogOptions? Resolve(
        ProtocolLogOptions? options, Func<string, string?> envReader, TextWriter? warnings = null)
    {
        var resolved = options;

        if (resolved is null || (string.IsNullOrWhiteSpace(resolved.Path) && resolved.Sink is null))
        {
            var fromEnv = envReader(EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(fromEnv))
                return null;

            resolved = (resolved ?? new ProtocolLogOptions()) with { Path = fromEnv.Trim() };
        }

        if (resolved.Path is { } path && string.Equals(path.Trim(), StandardOutput, StringComparison.OrdinalIgnoreCase))
        {
            // Standard output is the MCP server's JSON-RPC channel and the test runner's report, and
            // a line of protocol traffic on it would corrupt either.
            (warnings ?? Console.Error).WriteLine(
                $"Motus: the protocol log cannot be written to stdout, which carries the MCP channel "
                + $"and test output. Set {EnvironmentVariable} to a file path or to stderr.");
            resolved = resolved with { Path = null };
        }

        if (string.IsNullOrWhiteSpace(resolved.Path))
            resolved = resolved with { Path = null };

        return resolved.Path is null && resolved.Sink is null ? null : resolved;
    }

    /// <summary>
    /// Whether a destination names the standard error stream.
    /// </summary>
    internal static bool IsStandardError(string path)
        => string.Equals(path.Trim(), StandardError, StringComparison.OrdinalIgnoreCase);
}
