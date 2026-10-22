using Motus.Abstractions;

namespace Motus.Testing;

/// <summary>
/// Manages automatic trace capture around test execution.
/// When <c>failure.trace</c> is enabled in motus.config.json or via
/// the <c>MOTUS_FAILURES_TRACE</c> environment variable, tracing is
/// started before the test and saved to disk only when the test fails.
/// </summary>
/// <remarks>
/// Given a test name, the trace is saved as <c>trace-{name}-{timestamp}.zip</c> and failure
/// screenshots taken during the test as <c>failure-{name}-{timestamp}.png</c>, so each artifact
/// says which test it came from. The name is reduced to characters that are safe in a file name
/// on every OS and in CI artifact uploads, long names are shortened with a hash, and a name that
/// is already taken gets a <c>-2</c>, <c>-3</c> suffix rather than overwriting the earlier file.
/// </remarks>
public sealed class FailureTracing
{
    private static readonly IReadOnlyList<string> s_noPaths = [];

    private readonly string? _label;
    private bool _started;

    /// <summary>
    /// Creates a tracer whose artifacts carry only a timestamp in their names.
    /// </summary>
    public FailureTracing()
        : this(null)
    {
    }

    /// <summary>
    /// Creates a tracer whose artifacts are named after <paramref name="testName"/>,
    /// typically <c>{Class}.{Method}</c>.
    /// </summary>
    /// <param name="testName">The test to name artifacts after, or null for timestamp-only names.</param>
    public FailureTracing(string? testName)
    {
        _label = FailureArtifactName.Sanitize(testName);
    }

    /// <summary>
    /// The full path of the trace saved by the last <see cref="StopAsync"/>, or null when no trace
    /// was saved (tracing disabled, the test passed, or the trace could not be written).
    /// </summary>
    public string? TracePath { get; private set; }

    /// <summary>
    /// The full paths of the failure screenshots taken during the test, collected by the last
    /// <see cref="StopAsync"/> when the test failed. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// Screenshots are collected only when a test name was given, because that name is how the
    /// screenshots of one test are told apart from those of every other test sharing the browser.
    /// </remarks>
    public IReadOnlyList<string> ScreenshotPaths { get; private set; } = s_noPaths;

    /// <summary>
    /// Starts tracing on the context if failure tracing is enabled in config.
    /// Call this during test setup, after creating the context and page.
    /// </summary>
    public async Task StartIfEnabledAsync(IBrowserContext context)
    {
        TracePath = null;
        ScreenshotPaths = s_noPaths;

        // Screenshots are named whether or not traces are on, since they are configured separately.
        if (_label is not null && context is BrowserContext browserContext)
            browserContext.FailureArtifactLabel = _label;

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
    /// is saved to disk and its path is available from <see cref="TracePath"/>;
    /// otherwise it is discarded.
    /// Call this during test teardown, before closing the context.
    /// </summary>
    public async Task StopAsync(IBrowserContext context, bool testFailed)
    {
        TracePath = null;
        ScreenshotPaths = s_noPaths;

        if (context is BrowserContext browserContext)
        {
            // Drained either way, so a context that outlives this test does not hand these to the next.
            var screenshots = browserContext.DrainFailureScreenshots();
            if (testFailed)
                ScreenshotPaths = screenshots;
        }

        if (!_started)
            return;

        _started = false;

        string? filePath = null;
        try
        {
            if (testFailed)
            {
                var failure = MotusConfigLoader.Config.Failure;
                var basePath = failure?.TracePath ?? "test-results/traces";
                var fileName = FailureArtifactName.Build("trace", _label, "zip", DateTime.UtcNow);
                filePath = FailureArtifactName.ReservePath(basePath, fileName);

                await context.Tracing.StopAsync(new TracingStopOptions
                {
                    Path = filePath,
                }).ConfigureAwait(false);

                // A tracer that had nothing to stop returns without writing, leaving only the
                // reservation behind.
                DeleteIfEmpty(filePath);
                TracePath = File.Exists(filePath) ? filePath : null;
            }
            else
            {
                await context.Tracing.StopAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Trace capture must never mask the original test failure. The name reserved for a
            // trace that was never written would be an empty file posing as one.
            if (filePath is not null)
                DeleteIfEmpty(filePath);
        }
    }

    private static void DeleteIfEmpty(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (file.Exists && file.Length == 0)
                file.Delete();
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
