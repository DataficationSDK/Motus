using System.Reflection;
using Xunit;

namespace Motus.Testing.xUnit.Tests;

/// <summary>
/// xUnit builds class fixtures through their single public constructor and fills its parameters
/// from collection fixtures of exactly the parameter's type. These tests hold the adapter's types to
/// the shapes that rule allows, without launching a browser.
/// </summary>
public class FixtureShapeTests
{
    [Fact]
    public void BrowserContextFixture_HasSinglePublicParameterlessConstructor()
    {
        var ctor = Assert.Single(typeof(BrowserContextFixture).GetConstructors());
        Assert.Empty(ctor.GetParameters());
    }

    [Fact]
    public void BrowserContextFixtureOfT_HasSinglePublicConstructorTakingTheBrowserType()
    {
        var ctor = Assert.Single(typeof(BrowserContextFixture<SharedBrowserFixture>).GetConstructors());
        Assert.Equal(typeof(SharedBrowserFixture), Assert.Single(ctor.GetParameters()).ParameterType);

        var custom = Assert.Single(typeof(BrowserContextFixture<HeadlessBrowser>).GetConstructors());
        Assert.Equal(typeof(HeadlessBrowser), Assert.Single(custom.GetParameters()).ParameterType);
    }

    [Fact]
    public void SharedBrowserFixture_HasPublicParameterlessConstructor()
    {
        var ctor = Assert.Single(typeof(SharedBrowserFixture).GetConstructors());
        Assert.Empty(ctor.GetParameters());
    }

    [Fact]
    public void MotusCollection_ProvidesSharedBrowserFixture()
    {
        Assert.True(typeof(ICollectionFixture<SharedBrowserFixture>).IsAssignableFrom(typeof(MotusCollection)));
    }

    [Fact]
    public void SubclassChainingToSharedBrowserConstructor_StillBuilds()
    {
        var fixture = new ChainedFixture(new SharedBrowserFixture());

        // Nothing launches until InitializeAsync.
        Assert.Throws<InvalidOperationException>(() => fixture.Context);
        Assert.Throws<InvalidOperationException>(() => fixture.Page);
    }

    [Fact]
    public void SharedBrowserConstructor_IsProtected_AndTheGenericFixtureReplacesIt()
    {
        // Public in 1.1. The upgrade note in the testing guide gives the replacement for
        // `new BrowserContextFixture(browser)`; a change to either side belongs in both.
        var ctor = typeof(BrowserContextFixture).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, [typeof(SharedBrowserFixture)]);
        Assert.NotNull(ctor);
        Assert.True(ctor.IsFamily);

        var browser = new SharedBrowserFixture();
        BrowserContextFixture upgraded = new BrowserContextFixture<SharedBrowserFixture>(browser);
        Assert.Same(browser, Assert.IsType<BrowserContextFixture<SharedBrowserFixture>>(upgraded).SharedBrowser);
    }

    [Fact]
    public void SharedBrowserConstructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new ChainedFixture(null!));
        Assert.Throws<ArgumentNullException>(() => new BrowserContextFixture<SharedBrowserFixture>(null!));
    }

    [Fact]
    public async Task Dispose_BeforeInitialize_DoesNothing()
    {
        var fixture = new BrowserContextFixture();
        await fixture.DisposeAsync();
        Assert.Throws<InvalidOperationException>(() => fixture.Context);
    }

    // A context fixture subclass written before the parameterless constructor existed.
    private sealed class ChainedFixture : BrowserContextFixture
    {
        public ChainedFixture(SharedBrowserFixture browserFixture)
            : base(browserFixture)
        {
        }
    }

    private sealed class HeadlessBrowser : SharedBrowserFixture
    {
    }
}
