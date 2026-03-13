using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class TestDiscoveryTests
{
    [TestMethod]
    public void Discover_SelfAssembly_FindsTests()
    {
        var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = discovery.Discover([assemblyPath], null);

        Assert.IsTrue(tests.Count > 0, "Should discover tests from own assembly");
    }

    [TestMethod]
    public void Discover_SelfAssembly_ContainsThisTest()
    {
        var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = discovery.Discover([assemblyPath], null);

        Assert.IsTrue(tests.Any(t => t.FullName.Contains(nameof(Discover_SelfAssembly_ContainsThisTest))));
    }

    [TestMethod]
    public void Discover_FilterSubstring_FiltersCorrectly()
    {
        var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = discovery.Discover([assemblyPath], "ProtocolDiffer");

        Assert.IsTrue(tests.Count > 0);
        Assert.IsTrue(tests.All(t => t.FullName.Contains("ProtocolDiffer")));
    }

    [TestMethod]
    public void Discover_FilterNoMatch_ReturnsEmpty()
    {
        var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = discovery.Discover([assemblyPath], "ThisTestDoesNotExist_XYZ_123");

        Assert.AreEqual(0, tests.Count);
    }

    [TestMethod]
    public void Discover_NonexistentAssembly_PrintsError()
    {
        var discovery = new TestDiscovery();
        var tests = discovery.Discover(["/nonexistent/path.dll"], null);
        Assert.AreEqual(0, tests.Count);
    }
}
