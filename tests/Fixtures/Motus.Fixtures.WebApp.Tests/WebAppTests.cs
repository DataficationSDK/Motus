using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Motus.Fixtures.WebApp.Tests;

[TestClass]
public class WebAppTests
{
    private static WebApplicationFactory<Program> s_app = null!;

    // WebApplicationFactory looks for Motus.Fixtures.WebApp.deps.json under AppContext.BaseDirectory.
    [AssemblyInitialize]
    public static void Start(TestContext _) => s_app = new WebApplicationFactory<Program>();

    [AssemblyCleanup]
    public static void Stop() => s_app?.Dispose();

    [TestMethod]
    public async Task AppUsesItsOwnSkiaSharp()
        => Assert.AreEqual("3", await s_app.CreateClient().GetStringAsync("/skia"));

    [TestMethod]
    public void SuiteUsesItsOwnSkiaSharp()
        => Assert.AreEqual(3, typeof(SkiaSharp.SKBitmap).Assembly.GetName().Version!.Major);

    [TestMethod]
    public void BaseDirectoryIsTheOutputFolder()
        => Assert.AreEqual(OutputFolder, AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));

    [TestMethod]
    public void CurrentDirectoryIsTheOutputFolder()
        => Assert.AreEqual(OutputFolder, Environment.CurrentDirectory.TrimEnd(Path.DirectorySeparatorChar));

    private static string OutputFolder
        => Path.GetDirectoryName(typeof(WebAppTests).Assembly.Location)!.TrimEnd(Path.DirectorySeparatorChar);
}
