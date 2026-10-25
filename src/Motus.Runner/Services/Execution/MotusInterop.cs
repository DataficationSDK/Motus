using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Motus.Abstractions;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// Every call the test host makes into Motus, each one guarded.
/// </summary>
/// <remarks>
/// The test host runs a suite on the Motus its project references, which can be older (or newer)
/// than the one this runner was built against. A member the runner uses that the suite's Motus
/// does not have fails when the method that names it is compiled, so each such call is made from
/// a method of its own that is never inlined, from inside a guard. A call that cannot be made is
/// reported once and then skipped: the test still runs and reports its own result, and only what
/// the call would have added (an accessibility report, a performance metric, coverage) is missing.
/// </remarks>
internal static class MotusInterop
{
    private static readonly ConcurrentDictionary<string, bool> s_unavailable = new(StringComparer.Ordinal);

    /// <summary>Told once about each feature the suite's Motus cannot provide.</summary>
    public static Action<string>? Warn { get; set; }

    /// <summary>The version of the Motus assembly this process runs on, or null when there is none.</summary>
    public static Version? LoadedMotusVersion()
    {
        try
        {
            return Assembly.Load(new AssemblyName("Motus")).GetName().Version;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Publishes the running test method's name to Motus (for <c>[PerformanceBudget]</c> and artifact names).</summary>
    public static void SetTestMethodName(string name) => Guard("test method names", () => SetTestMethodNameCore(name));

    /// <summary>Clears what <see cref="SetTestMethodName"/> published.</summary>
    public static void ClearTestMethodName() => Guard("test method names", ClearTestMethodNameCore);

    /// <summary>Starts collecting accessibility violations, performance metrics and coverage for this async flow.</summary>
    public static void BeginCollecting()
    {
        Guard("accessibility reports", BeginViolationsCore);
        Guard("performance metrics", BeginMetricsCore);
        Guard("coverage", BeginCoverageCore);
    }

    /// <summary>
    /// Ends what <see cref="BeginCollecting"/> started and returns what was collected as JSON, so that
    /// it crosses to the runner without naming the suite's Motus types.
    /// </summary>
    public static (JsonElement? Violations, JsonElement? Metrics, JsonElement? Coverage) EndCollecting()
    {
        JsonElement? violations = null, metrics = null, coverage = null;
        Guard("accessibility reports", () => violations = EndViolationsCore());
        Guard("performance metrics", () => metrics = EndMetricsCore());
        Guard("coverage", () => coverage = EndCoverageCore());
        return (violations, metrics, coverage);
    }

    /// <summary>
    /// True when the exception chain says the browser connection was lost, which the default retry
    /// policy re-runs. Falls back to the exceptions' type names on a Motus without these types.
    /// </summary>
    public static bool IsBrowserLost(Exception? exception)
    {
        if (exception is null)
            return false;

        var lost = false;
        if (Guard("lost browser detection", () => lost = IsBrowserLostCore(exception)))
            return lost;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.GetType().Name is "MotusTargetClosedException" or "CdpDisconnectedException")
                return true;
        }

        return false;
    }

    /// <summary>Reads <c>motus.config.json</c> now, so that it is read from the current directory.</summary>
    public static void LoadConfig() => Guard("motus.config.json", LoadConfigCore);

    /// <summary>
    /// Runs <paramref name="call"/> unless <paramref name="feature"/> is known to be missing. Returns
    /// false, and says so once, when the suite's Motus does not have what it needs.
    /// </summary>
    internal static bool Guard(string feature, Action call)
    {
        if (s_unavailable.ContainsKey(feature))
            return false;

        try
        {
            call();
            return true;
        }
        catch (Exception ex) when (IsMissingFromMotus(ex))
        {
            if (s_unavailable.TryAdd(feature, true))
            {
                var version = LoadedMotusVersion()?.ToString(3) ?? "unknown";
                Warn?.Invoke($"The Motus {version} this suite references has no support for {feature} that this motus can use, "
                    + $"so the run goes on without it. ({ex.GetType().Name}: {ex.Message})");
            }

            return false;
        }
    }

    /// <summary>The exceptions the runtime throws when compiled code names something an assembly lacks.</summary>
    internal static bool IsMissingFromMotus(Exception ex)
        => ex is TypeLoadException or MissingMemberException or FileNotFoundException or FileLoadException or BadImageFormatException;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SetTestMethodNameCore(string name) => TestMethodNameContext.Set(name);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClearTestMethodNameCore() => TestMethodNameContext.Clear();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BeginViolationsCore() => AccessibilityViolationSink.Begin();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BeginMetricsCore() => PerformanceMetricsSink.Begin();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void BeginCoverageCore() => CoverageSink.Begin();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsonElement? EndViolationsCore()
    {
        var violations = AccessibilityViolationSink.End();
        return violations.Count > 0 ? JsonSerializer.SerializeToElement(violations, TestHostJson.Options) : null;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsonElement? EndMetricsCore()
        => PerformanceMetricsSink.End() is { } metrics ? JsonSerializer.SerializeToElement(metrics, TestHostJson.Options) : null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static JsonElement? EndCoverageCore()
        => CoverageSink.End() is { } coverage ? JsonSerializer.SerializeToElement(coverage, TestHostJson.Options) : null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool IsBrowserLostCore(Exception exception)
    {
        if (BrowserFailure.IsBrowserLost(exception))
            return true;

        // The transport's own disconnect reaches the runner unwrapped when it escapes from outside
        // a command, where nothing has turned it into the public failure yet.
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is CdpDisconnectedException)
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void LoadConfigCore() => _ = MotusConfigLoader.Config;
}
