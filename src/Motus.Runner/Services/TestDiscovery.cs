using Motus.Runner.Services.Execution;
using Motus.Runner.Services.Models;

namespace Motus.Runner.Services;

/// <summary>
/// Finds the tests in test assemblies for the visual runner. Each assembly is loaded by a test host
/// process of its own, started from the assembly's output folder with its own dependencies, which
/// exits once it has listed the tests. Runs start a fresh one (see <see cref="TestExecutionService"/>),
/// so a rebuilt assembly is picked up by the next run.
/// </summary>
public sealed class TestDiscovery(ILogger<TestDiscovery> logger)
{
    /// <summary>
    /// The tests in <paramref name="assemblyPaths"/> whose full name contains <paramref name="filter"/>,
    /// ignoring case. An assembly that cannot be loaded is logged and skipped.
    /// </summary>
    public async Task<List<DiscoveredTest>> DiscoverAsync(string[] assemblyPaths, string? filter, CancellationToken cancellationToken = default)
    {
        var tests = new List<DiscoveredTest>();

        foreach (var path in assemblyPaths)
        {
            var fullPath = Path.GetFullPath(path);
            try
            {
                await using var host = await TestHostClient.StartAsync(
                    fullPath,
                    new TestHostOptions { OnLog = message => logger.LogWarning("{Message}", message) },
                    cancellationToken);
                var discovery = await host.DiscoverAsync(filter, cancellationToken);

                foreach (var test in discovery.Tests)
                {
                    tests.Add(new DiscoveredTest(
                        TestClass: null,
                        TestMethod: null,
                        test.FullName,
                        discovery.AssemblyName,
                        test.IsIgnored,
                        test.IgnoreReason,
                        CodeBase: fullPath,
                        ClassFullName: test.ClassName,
                        MethodName: test.MethodName));
                }
            }
            catch (TestHostException ex)
            {
                logger.LogError("Failed to load assembly {Path}: {Message}", fullPath, ex.Message);
            }
        }

        return tests;
    }
}
