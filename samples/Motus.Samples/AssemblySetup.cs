namespace Motus.Samples;

[TestClass]
public class AssemblySetup
{
    [AssemblyInitialize]
    public static async Task Initialize(TestContext _) =>
        await MotusTestBase.LaunchBrowserAsync(new LaunchOptions { Headless = true });

    [AssemblyCleanup]
    public static async Task Cleanup() =>
        await MotusTestBase.CloseBrowserAsync();
}
