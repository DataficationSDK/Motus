using System.Reflection;
using Motus.Runner.Services.Execution;

namespace Motus.Cli.Services;

/// <summary>
/// A test case found in a test assembly. <paramref name="Arguments"/> is the data row of one case of a
/// data-driven test, or null for a test without one.
/// </summary>
/// <remarks>
/// A test found by <see cref="TestDiscovery"/> lives in a test host process of its own assembly, so
/// <paramref name="TestClass"/>, <paramref name="TestMethod"/> and <paramref name="Arguments"/> are
/// null and the test is known by its names. One built from reflection metadata runs in this process.
/// </remarks>
public sealed record DiscoveredTest(Type? TestClass, MethodInfo? TestMethod, string FullName, bool IsIgnored, bool Quarantined = false, object?[]? Arguments = null)
{
    /// <summary>The full name of the test class.</summary>
    public string ClassName { get; init; } = TestClass?.FullName ?? string.Empty;

    /// <summary>The test method's name, without a data row.</summary>
    public string MethodName { get; init; } = TestMethod?.Name ?? string.Empty;

    /// <summary>The full path of the test assembly.</summary>
    public string AssemblyPath { get; init; } = TestClass?.Assembly.Location ?? string.Empty;

    /// <summary>The test assembly's name, which reporters use as the suite name.</summary>
    public string AssemblyName { get; init; } = TestClass?.Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Identifies the test apart from its name: the class, the method with its parameter types, and
    /// for a data row, its data source and its place in it. A data source can name its rows differently
    /// each time it is read, so sharding keys on this rather than on the name.
    /// </summary>
    public string TestId { get; init; } = FullName;

    /// <summary>The test host that discovered this test and runs it, or null to run it in this process.</summary>
    internal ITestAssemblyHost? Host { get; init; }
}

/// <summary>
/// Finds the tests in test assemblies. Each assembly is loaded by a test host process of its own,
/// started from the assembly's output folder with its own dependencies, which stays up to run the
/// tests and is shut down when this is disposed.
/// </summary>
public sealed class TestDiscovery : IAsyncDisposable
{
    private readonly List<TestHostClient> _hosts = [];
    private readonly List<string> _failedAssemblies = [];

    /// <summary>
    /// The assemblies passed to <see cref="DiscoverAsync"/> that could not be loaded, or whose test
    /// host could not start or exited during discovery. <c>motus run</c> fails the run for them.
    /// </summary>
    public IReadOnlyList<string> FailedAssemblies => _failedAssemblies;

    /// <summary>
    /// The tests in <paramref name="assemblyPaths"/> whose full name contains <paramref name="filter"/>,
    /// ignoring case. An assembly that cannot be loaded is reported on standard error, skipped, and
    /// listed in <see cref="FailedAssemblies"/>.
    /// </summary>
    public async Task<List<DiscoveredTest>> DiscoverAsync(string[] assemblyPaths, string? filter, CancellationToken cancellationToken = default)
    {
        var tests = new List<DiscoveredTest>();

        foreach (var path in assemblyPaths)
        {
            var fullPath = Path.GetFullPath(path);
            TestHostClient? host = null;
            try
            {
                host = await TestHostClient.StartAsync(fullPath, new TestHostOptions { OnLog = Console.Error.WriteLine }, cancellationToken);
                var discovery = await host.DiscoverAsync(filter, cancellationToken);

                if (discovery.Tests.Count == 0)
                {
                    await host.DisposeAsync();
                    continue;
                }

                _hosts.Add(host);
                foreach (var test in discovery.Tests)
                {
                    tests.Add(new DiscoveredTest(null, null, test.FullName, test.IsIgnored, test.Quarantined)
                    {
                        ClassName = test.ClassName,
                        MethodName = test.MethodName,
                        AssemblyPath = fullPath,
                        AssemblyName = discovery.AssemblyName,
                        TestId = test.Id,
                        Host = host,
                    });
                }
            }
            catch (TestHostException ex)
            {
                Console.Error.WriteLine($"Failed to load assembly {fullPath}: {ex.Message}");
                _failedAssemblies.Add(fullPath);
                if (host is not null)
                    await host.DisposeAsync();
            }
        }

        return tests;
    }

    /// <summary>Shuts down the test host processes.</summary>
    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();
        _hosts.Clear();
    }
}
