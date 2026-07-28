using Motus.Abstractions;

namespace Motus.Tests.Browser;

[TestClass]
public class ConnectOptionsTests
{
    [TestMethod]
    public void Defaults_AdoptExistingTargets()
    {
        var options = new ConnectOptions();

        Assert.IsTrue(options.AdoptExistingTargets);
        Assert.AreEqual(0, options.SlowMo);
        Assert.AreEqual(30_000, options.Timeout);
    }

    [TestMethod]
    public void InitProperties_AreSettable()
    {
        var options = new ConnectOptions
        {
            AdoptExistingTargets = false,
            SlowMo = 250,
            Timeout = 5_000
        };

        Assert.IsFalse(options.AdoptExistingTargets);
        Assert.AreEqual(250, options.SlowMo);
        Assert.AreEqual(5_000, options.Timeout);
    }

    [TestMethod]
    public async Task ConnectAsync_RejectsAnEndpointThatIsNotAUrl()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => MotusLauncher.ConnectAsync("not-an-endpoint", new ConnectOptions()));
    }

    [TestMethod]
    public async Task ConnectAsync_RejectsAnUnsupportedScheme()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(
            () => MotusLauncher.ConnectAsync("file:///tmp/browser", new ConnectOptions()));
    }
}
