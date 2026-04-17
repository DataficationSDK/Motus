using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class LocatorDispatcherTests
{
    [TestMethod]
    public void NormalizeRole_AriaRoleDotButton_ReturnsButton()
    {
        Assert.AreEqual("button", LocatorDispatcher.NormalizeRole("AriaRole.Button"));
    }

    [TestMethod]
    public void NormalizeRole_AriaRoleDotTextbox_ReturnsTextbox()
    {
        Assert.AreEqual("textbox", LocatorDispatcher.NormalizeRole("AriaRole.Textbox"));
    }

    [TestMethod]
    public void NormalizeRole_LowercasePlainString_Unchanged()
    {
        Assert.AreEqual("button", LocatorDispatcher.NormalizeRole("button"));
    }

    [TestMethod]
    public void NormalizeRole_MixedCaseWithoutPrefix_Lowercased()
    {
        Assert.AreEqual("button", LocatorDispatcher.NormalizeRole("Button"));
    }

    [TestMethod]
    public void NormalizeRole_EmptyString_ReturnsEmpty()
    {
        Assert.AreEqual("", LocatorDispatcher.NormalizeRole(""));
    }

    [TestMethod]
    public void NormalizeRole_MultipleDots_TakesLastSegment()
    {
        Assert.AreEqual("button", LocatorDispatcher.NormalizeRole("Foo.Bar.Button"));
    }
}
