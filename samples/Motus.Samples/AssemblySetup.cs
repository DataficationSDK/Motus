namespace Motus.Samples;

/// <summary>
/// Launches the shared browser up front with options set in code. Optional: without it,
/// <see cref="MotusTestBase"/> launches the browser with the configured defaults on first use and
/// closes it when the run ends. It is here because these samples enable performance collection.
/// </summary>
[TestClass]
public class AssemblySetup
{
    [AssemblyInitialize]
    public static async Task Initialize(TestContext _) =>
        await MotusTestBase.LaunchBrowserAsync(new LaunchOptions
        {
            Headless = true,
            Performance = new PerformanceOptions { Enable = true },
        });

    [AssemblyCleanup]
    public static async Task Cleanup() =>
        await MotusTestBase.CloseBrowserAsync();
}
