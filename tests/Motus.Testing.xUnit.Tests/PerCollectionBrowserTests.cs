using Motus.Abstractions;
using Xunit;

namespace Motus.Testing.xUnit.Tests;

// A collection with a browser of its own, declared the way the docs describe: a one-line
// definition in the test project deriving from MotusCollection, and classes that take their
// context from it through BrowserContextFixture<SharedBrowserFixture>.

[CollectionDefinition(Name)]
public class PerCollectionBrowser : MotusCollection
{
    public const string Name = "Per-collection browser";
}

public abstract class PerCollectionBrowserTestBase : IClassFixture<BrowserContextFixture<SharedBrowserFixture>>
{
    private readonly BrowserContextFixture<SharedBrowserFixture> _fixture;
    private readonly SharedBrowserFixture _browser;

    protected PerCollectionBrowserTestBase(BrowserContextFixture<SharedBrowserFixture> fixture, SharedBrowserFixture browser)
    {
        _fixture = fixture;
        _browser = browser;
    }

    [Fact]
    public async Task UsesTheCollectionBrowser()
    {
        Assert.Same(_browser, _fixture.SharedBrowser);
        Assert.Same(_browser.Browser, _fixture.Context.Browser);
        await TestPages.ShowTitleAsync(_fixture.Page, GetType().Name);
    }
}

[Collection(PerCollectionBrowser.Name)]
[Trait("TestCategory", "Integration")]
public class PerCollectionBrowserTests1(BrowserContextFixture<SharedBrowserFixture> fixture, SharedBrowserFixture browser)
    : PerCollectionBrowserTestBase(fixture, browser);

[Collection(PerCollectionBrowser.Name)]
[Trait("TestCategory", "Integration")]
public class PerCollectionBrowserTests2(BrowserContextFixture<SharedBrowserFixture> fixture, SharedBrowserFixture browser)
    : PerCollectionBrowserTestBase(fixture, browser);

// A collection whose browser is launched with options of its own, through a SharedBrowserFixture
// subclass that the context fixture names as its type argument.

public class CustomUserAgentBrowser : SharedBrowserFixture
{
    public const string UserAgent = "MotusCustomBrowser/1.0";

    protected override LaunchOptions? LaunchOptions => new() { Args = [$"--user-agent={UserAgent}"] };
}

[CollectionDefinition(Name)]
public class CustomUserAgentCollection : ICollectionFixture<CustomUserAgentBrowser>
{
    public const string Name = "Custom launch options browser";
}

[Collection(CustomUserAgentCollection.Name)]
[Trait("TestCategory", "Integration")]
public class CustomLaunchOptionsTests : IClassFixture<BrowserContextFixture<CustomUserAgentBrowser>>
{
    private readonly BrowserContextFixture<CustomUserAgentBrowser> _fixture;
    private readonly CustomUserAgentBrowser _browser;

    public CustomLaunchOptionsTests(BrowserContextFixture<CustomUserAgentBrowser> fixture, CustomUserAgentBrowser browser)
    {
        _fixture = fixture;
        _browser = browser;
    }

    [Fact]
    public async Task LaunchesWithTheSubclassOptions()
    {
        Assert.Same(_browser.Browser, _fixture.Context.Browser);

        await TestPages.ShowTitleAsync(_fixture.Page, nameof(CustomLaunchOptionsTests));
        var userAgent = await _fixture.Page.EvaluateAsync<string>("navigator.userAgent");
        Assert.Equal(CustomUserAgentBrowser.UserAgent, userAgent);
    }
}
