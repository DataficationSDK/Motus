using Motus.Testing.MSTest;

namespace Motus.Testing.MSTest.v4.Tests;

/// <summary>
/// The sample that MSTest 4 support was asked for with: a class deriving from
/// <see cref="MotusTestBase"/> that writes to its <see cref="MotusTestBase.TestContext"/>. Built
/// against the MSTest 3 package it does not compile, and without the TestContext line it compiles
/// and is then never discovered.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class SmokeTests : MotusTestBase
{
    [TestMethod]
    public async Task Opens()
    {
        TestContext.WriteLine("starting");
        await Page.GotoAsync("data:text/html,<p>hi</p>");

        Assert.AreEqual("hi", await Page.Locator("p").TextContentAsync());
    }

    [TestMethod]
    public void TheTestContext_IsTheOneMSTestSetForThisTest()
        => Assert.AreEqual(nameof(TheTestContext_IsTheOneMSTestSetForThisTest), TestContext.TestName);
}

/// <summary>
/// The same through <see cref="MotusTestClassAttribute"/>, which hands every test to
/// <see cref="MotusTestMethodAttribute"/> and so runs it through that attribute's MSTest 4 path.
/// </summary>
[MotusTestClass]
[TestCategory("Integration")]
public class MotusTestClassSmokeTests : MotusTestBase
{
    [TestMethod]
    public async Task Opens()
    {
        TestContext.WriteLine("starting");
        await Page.GotoAsync("data:text/html,<p>hi</p>");

        Assert.AreEqual("hi", await Page.Locator("p").TextContentAsync());
    }
}
