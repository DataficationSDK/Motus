using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class TestDiscoveryTests
{
    [TestMethod]
    public async Task Discover_SelfAssembly_FindsTests()
    {
        await using var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = await discovery.DiscoverAsync([assemblyPath], null);

        Assert.IsTrue(tests.Count > 0, "Should discover tests from own assembly");
    }

    [TestMethod]
    public async Task Discover_SelfAssembly_ContainsThisTest()
    {
        await using var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = await discovery.DiscoverAsync([assemblyPath], null);

        var test = tests.Single(t => t.FullName.EndsWith("." + nameof(Discover_SelfAssembly_ContainsThisTest), StringComparison.Ordinal));
        Assert.AreEqual(typeof(TestDiscoveryTests).FullName, test.ClassName);
        Assert.AreEqual(nameof(Discover_SelfAssembly_ContainsThisTest), test.MethodName);
        Assert.AreEqual(assemblyPath, test.AssemblyPath);
        Assert.AreEqual(typeof(TestDiscoveryTests).Assembly.GetName().Name, test.AssemblyName);
        Assert.IsNull(test.TestClass, "a discovered test lives in its test host, not in this process");
    }

    [TestMethod]
    public async Task Discover_FilterSubstring_FiltersCorrectly()
    {
        await using var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = await discovery.DiscoverAsync([assemblyPath], "ProtocolDiffer");

        Assert.IsTrue(tests.Count > 0);
        Assert.IsTrue(tests.All(t => t.FullName.Contains("ProtocolDiffer")));
    }

    [TestMethod]
    public async Task Discover_FilterNoMatch_ReturnsEmpty()
    {
        await using var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = await discovery.DiscoverAsync([assemblyPath], "ThisTestDoesNotExist_XYZ_123");

        Assert.AreEqual(0, tests.Count);
    }

    [TestMethod]
    public async Task Discover_NonexistentAssembly_PrintsError()
    {
        await using var discovery = new TestDiscovery();
        var tests = await discovery.DiscoverAsync(["/nonexistent/path.dll"], null);
        Assert.AreEqual(0, tests.Count);
    }

    [TestMethod]
    public async Task Discover_IgnoredMethod_MarkedAsIgnored()
    {
        await using var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = await discovery.DiscoverAsync([assemblyPath], nameof(IgnoredFixture));

        var ignored = tests.FirstOrDefault(t => t.FullName.Contains(nameof(IgnoredFixture.SkippedTest)));
        Assert.IsNotNull(ignored, "Should discover the ignored test");
        Assert.IsTrue(ignored.IsIgnored, "Test with [Ignore] should have IsIgnored = true");

        var normal = tests.FirstOrDefault(t => t.FullName.Contains(nameof(IgnoredFixture.NormalTest)));
        Assert.IsNotNull(normal, "Should discover the normal test");
        Assert.IsFalse(normal.IsIgnored, "Test without [Ignore] should have IsIgnored = false");
    }

    [TestMethod]
    public async Task Discover_IgnoredClass_AllTestsMarkedAsIgnored()
    {
        await using var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = await discovery.DiscoverAsync([assemblyPath], nameof(IgnoredClassFixture));

        Assert.IsTrue(tests.Count > 0, "Should discover tests from ignored class");
        Assert.IsTrue(tests.All(t => t.IsIgnored), "All tests from [Ignore] class should be ignored");
    }

    [TestMethod]
    public async Task Discover_QuarantinedMethod_MarkedAsQuarantined()
    {
        await using var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = await discovery.DiscoverAsync([assemblyPath], nameof(QuarantinedFixture));

        var quarantined = tests.FirstOrDefault(t => t.FullName.Contains(nameof(QuarantinedFixture.QuarantinedTest)));
        Assert.IsNotNull(quarantined, "Should discover the quarantined test");
        Assert.IsTrue(quarantined.Quarantined, "Test with [Quarantine] should have Quarantined = true");

        var normal = tests.FirstOrDefault(t => t.FullName.Contains(nameof(QuarantinedFixture.NormalTest)));
        Assert.IsNotNull(normal, "Should discover the normal test");
        Assert.IsFalse(normal.Quarantined, "Test without [Quarantine] should have Quarantined = false");
    }

    [TestMethod]
    public async Task Discover_QuarantinedClass_AllTestsMarkedAsQuarantined()
    {
        await using var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = await discovery.DiscoverAsync([assemblyPath], nameof(QuarantinedClassFixture));

        Assert.IsTrue(tests.Count > 0, "Should discover tests from quarantined class");
        Assert.IsTrue(tests.All(t => t.Quarantined), "All tests from [Quarantine] class should be quarantined");
    }

    [TestClass]
    public class IgnoredFixture
    {
        [TestMethod]
        public void NormalTest() { }

        [TestMethod]
        [Ignore]
        public void SkippedTest() { }
    }

    [TestClass]
    [Ignore]
    public class IgnoredClassFixture
    {
        [TestMethod]
        public void AnyTest() { }
    }

    [TestClass]
    public class QuarantinedFixture
    {
        [TestMethod]
        public void NormalTest() { }

        [TestMethod]
        [Motus.Abstractions.Quarantine]
        public void QuarantinedTest() { }
    }

    [TestClass]
    [Motus.Abstractions.Quarantine]
    public class QuarantinedClassFixture
    {
        [TestMethod]
        public void AnyTest() { }
    }
}
