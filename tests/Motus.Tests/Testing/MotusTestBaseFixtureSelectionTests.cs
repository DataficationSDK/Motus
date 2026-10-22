using Motus.Abstractions;
using Motus.Testing;
using Motus.Testing.MSTest;

namespace Motus.Tests.Testing;

[TestClass]
public class MotusTestBaseFixtureSelectionTests
{
    // Not [TestClass]: these exist only to be resolved, never run.
    private sealed class NoOverride : MotusTestBase;

    private sealed class NullOverride : MotusTestBase
    {
        protected override LaunchOptions? LaunchOptions => null;
    }

    private abstract class SlowAppTests : MotusTestBase
    {
        protected override LaunchOptions? LaunchOptions => new() { SlowMo = 50 };
    }

    private sealed class CartTests : SlowAppTests;

    private sealed class CheckoutTests : SlowAppTests;

    private sealed class HeadedCheckoutTests : SlowAppTests
    {
        protected override LaunchOptions? LaunchOptions => new() { Headless = false };
    }

    private class HidingTests : MotusTestBase
    {
        // Hides rather than overrides, so the base class never sees it.
        protected new LaunchOptions? LaunchOptions => new() { SlowMo = 1 };
    }

    [TestMethod]
    public void NoOverride_UsesTheSharedBrowser()
    {
        var fixture = new NoOverride().ResolveFixture(out _);

        Assert.AreSame(MotusTestBase.SharedFixture, fixture);
    }

    [TestMethod]
    public void OverrideReturningNull_UsesTheSharedBrowser()
    {
        Assert.AreSame(MotusTestBase.SharedFixture, new NullOverride().ResolveFixture(out _));
    }

    [TestMethod]
    public void ClassesInheritingOneOverride_ShareABrowserOfTheirOwn()
    {
        var cart = new CartTests().ResolveFixture(out var cartOptions);
        var checkout = new CheckoutTests().ResolveFixture(out _);

        Assert.AreSame(cart, checkout);
        Assert.AreNotSame(MotusTestBase.SharedFixture, cart);
        Assert.AreEqual(50, cartOptions?.SlowMo);
    }

    [TestMethod]
    public void ClassThatOverridesAgain_GetsItsOwnBrowser()
    {
        var cart = new CartTests().ResolveFixture(out _);
        var headed = new HeadedCheckoutTests().ResolveFixture(out var options);

        Assert.AreNotSame(cart, headed);
        Assert.AreNotSame(MotusTestBase.SharedFixture, headed);
        Assert.AreEqual(false, options?.Headless);
    }

    [TestMethod]
    public void FindLaunchOptionsOwner_IsTheDeclaringClass()
    {
        Assert.AreEqual(typeof(SlowAppTests), MotusTestBase.FindLaunchOptionsOwner(typeof(CartTests)));
        Assert.AreEqual(typeof(HeadedCheckoutTests), MotusTestBase.FindLaunchOptionsOwner(typeof(HeadedCheckoutTests)));
        Assert.IsNull(MotusTestBase.FindLaunchOptionsOwner(typeof(NoOverride)));
        Assert.IsNull(MotusTestBase.FindLaunchOptionsOwner(typeof(HidingTests)));
    }
}
