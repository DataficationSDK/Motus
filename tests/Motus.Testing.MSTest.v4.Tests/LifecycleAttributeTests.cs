using System.Reflection;
using Motus.Testing.MSTest;

namespace Motus.Testing.MSTest.v4.Tests;

/// <summary>
/// MSTest finds an inherited <c>[TestInitialize]</c> and <c>[TestCleanup]</c>, and sets
/// <c>TestContext</c>, by the exact identity of its own types. These hold when the base class was
/// built against the MSTest this project runs on, and fail when it was built against another major.
/// </summary>
[TestClass]
public class LifecycleAttributeTests
{
    [TestMethod]
    public void SetupAndCleanup_AreMarkedWithThisMSTestsAttributes()
    {
        var setup = typeof(MotusTestBase).GetMethod(nameof(MotusTestBase.MotusTestInitialize));
        var cleanup = typeof(MotusTestBase).GetMethod(nameof(MotusTestBase.MotusTestCleanup));

        Assert.IsNotNull(setup?.GetCustomAttribute<TestInitializeAttribute>(), "MSTest would never run the setup");
        Assert.IsNotNull(cleanup?.GetCustomAttribute<TestCleanupAttribute>(), "MSTest would never run the cleanup");
    }

    [TestMethod]
    public void TheTestContextProperty_IsThisMSTestsTestContext()
    {
        var property = typeof(MotusTestBase).GetProperty(nameof(MotusTestBase.TestContext));

        Assert.AreEqual(typeof(TestContext), property?.PropertyType);
    }
}
