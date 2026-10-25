using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Motus.Fixtures.OldMotus.Tests;

[TestClass]
public class OldMotusTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public void Passes() => Assert.AreEqual(2, 1 + 1);

    [TestMethod]
    [DataRow("a")]
    [DataRow("b")]
    public void Rows(string value) => Assert.IsTrue(value is "a" or "b");

    [TestMethod]
    public void HasContext() => Assert.AreEqual(nameof(HasContext), TestContext.TestName);

    // Fails only for the test host that TestHostIsolationTests starts, so that the runner's verdict
    // can be checked against a known failure; under dotnet test it passes.
    [TestMethod]
    public void FailsWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("MOTUS_OLD_MOTUS_FIXTURE_FAIL") == "1")
            Assert.Fail("failed as asked");
    }

    [TestMethod]
    public void UsesItsOwnMotus()
        => Assert.AreEqual(new Version(1, 0, 1, 0), typeof(Motus.Testing.MSTest.MotusTestBase).Assembly.GetName().Version);
}
