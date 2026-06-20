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

    [TestMethod]
    public void Discover_IgnoredMethod_MarkedAsIgnored()
    {
        var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = discovery.Discover([assemblyPath], nameof(IgnoredFixture));

        var ignored = tests.FirstOrDefault(t => t.FullName.Contains(nameof(IgnoredFixture.SkippedTest)));
        Assert.IsNotNull(ignored, "Should discover the ignored test");
        Assert.IsTrue(ignored.IsIgnored, "Test with [Ignore] should have IsIgnored = true");

        var normal = tests.FirstOrDefault(t => t.FullName.Contains(nameof(IgnoredFixture.NormalTest)));
        Assert.IsNotNull(normal, "Should discover the normal test");
        Assert.IsFalse(normal.IsIgnored, "Test without [Ignore] should have IsIgnored = false");
    }

    [TestMethod]
    public void Discover_IgnoredClass_AllTestsMarkedAsIgnored()
    {
        var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = discovery.Discover([assemblyPath], nameof(IgnoredClassFixture));

        Assert.IsTrue(tests.Count > 0, "Should discover tests from ignored class");
        Assert.IsTrue(tests.All(t => t.IsIgnored), "All tests from [Ignore] class should be ignored");
    }

    [TestMethod]
    public void Discover_QuarantinedMethod_MarkedAsQuarantined()
    {
        var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = discovery.Discover([assemblyPath], nameof(QuarantinedFixture));

        var quarantined = tests.FirstOrDefault(t => t.FullName.Contains(nameof(QuarantinedFixture.QuarantinedTest)));
        Assert.IsNotNull(quarantined, "Should discover the quarantined test");
        Assert.IsTrue(quarantined.Quarantined, "Test with [Quarantine] should have Quarantined = true");

        var normal = tests.FirstOrDefault(t => t.FullName.Contains(nameof(QuarantinedFixture.NormalTest)));
        Assert.IsNotNull(normal, "Should discover the normal test");
        Assert.IsFalse(normal.Quarantined, "Test without [Quarantine] should have Quarantined = false");
    }

    [TestMethod]
    public void Discover_QuarantinedClass_AllTestsMarkedAsQuarantined()
    {
        var discovery = new TestDiscovery();
        var assemblyPath = typeof(TestDiscoveryTests).Assembly.Location;

        var tests = discovery.Discover([assemblyPath], nameof(QuarantinedClassFixture));

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
