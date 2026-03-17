using Motus.Abstractions;

namespace Motus.Testing;

/// <summary>
/// Manages automatic trace capture around test execution.
/// When <c>failure.trace</c> is enabled in motus.config.json or via
/// the <c>MOTUS_FAILURES_TRACE</c> environment variable, tracing is
/// started before the test and saved to disk only when the test fails.
/// </summary>
public sealed class FailureTracing
{
    private bool _started;

    /// <summary>
    /// Starts tracing on the context if failure tracing is enabled in config.
    /// Call this during test setup, after creating the context and page.
    /// </summary>
    public async Task StartIfEnabledAsync(IBrowserContext context)
    {
        var failure = MotusConfigLoader.Config.Failure;
        if (failure is null || failure.Trace is not true)
            return;

        try
        {
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
            }).ConfigureAwait(false);
            _started = true;
        }
        catch
        {
            // Trace start must never prevent the test from running
        }
    }

    /// <summary>
    /// Stops tracing. If <paramref name="testFailed"/> is true, the trace
    /// is saved to disk; otherwise it is discarded.
    /// Call this during test teardown, before closing the context.
    /// </summary>
    public async Task StopAsync(IBrowserContext context, bool testFailed)
    {
        if (!_started)
            return;

        _started = false;

        try
        {
            if (testFailed)
            {
                var failure = MotusConfigLoader.Config.Failure;
                var basePath = failure?.TracePath ?? "test-results/traces";
                Directory.CreateDirectory(basePath);
                var fileName = $"trace-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.zip";
                var filePath = Path.Combine(basePath, fileName);

                await context.Tracing.StopAsync(new TracingStopOptions
                {
                    Path = filePath,
                }).ConfigureAwait(false);
            }
            else
            {
                await context.Tracing.StopAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Trace capture must never mask the original test failure
        }
    }
}
