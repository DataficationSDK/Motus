using Xunit;

namespace Motus.Testing.xUnit.Tests;

// Classes that declare IClassFixture<BrowserContextFixture> and nothing else, with no collection.
// Each is its own collection, so xUnit runs them in parallel, more of them than the four contexts
// the browser fixtures allow open at once. All of them get their context from one browser that
// the first to start launches.

public abstract class ZeroConfigTestBase : IClassFixture<BrowserContextFixture>
{
    private readonly BrowserContextFixture _fixture;

    protected ZeroConfigTestBase(BrowserContextFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void SharesOneBrowserAcrossTheProcess()
    {
        DefaultBrowserObserver.AssertSingle(_fixture.Context.Browser);
    }

    [Fact]
    public async Task NavigatesInTheClassContext()
    {
        Assert.Same(_fixture.Context, _fixture.Page.Context);
        await TestPages.ShowTitleAsync(_fixture.Page, GetType().Name);
    }
}

[Trait("TestCategory", "Integration")]
public class ZeroConfigTests1(BrowserContextFixture fixture) : ZeroConfigTestBase(fixture);

[Trait("TestCategory", "Integration")]
public class ZeroConfigTests2(BrowserContextFixture fixture) : ZeroConfigTestBase(fixture);

[Trait("TestCategory", "Integration")]
public class ZeroConfigTests3(BrowserContextFixture fixture) : ZeroConfigTestBase(fixture);

[Trait("TestCategory", "Integration")]
public class ZeroConfigTests4(BrowserContextFixture fixture) : ZeroConfigTestBase(fixture);

[Trait("TestCategory", "Integration")]
public class ZeroConfigTests5(BrowserContextFixture fixture) : ZeroConfigTestBase(fixture);

[Trait("TestCategory", "Integration")]
public class ZeroConfigTests6(BrowserContextFixture fixture) : ZeroConfigTestBase(fixture);
