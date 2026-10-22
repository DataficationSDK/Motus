using Xunit;

namespace Motus.Testing.xUnit.Tests;

// Code written to the setup the docs described before the class fixture worked on its own:
// [Collection(nameof(MotusCollection))] with IClassFixture<BrowserContextFixture>, and no
// collection definition in this assembly. The classes share one collection, so they run one after
// another, and each closes its context before the next opens one. Six of them is more than the
// four contexts allowed open at once, so a context that is closed without returning its slot
// stops the fifth class from ever starting.

public abstract class MotusCollectionAttributeTestBase : IClassFixture<BrowserContextFixture>
{
    private readonly BrowserContextFixture _fixture;

    protected MotusCollectionAttributeTestBase(BrowserContextFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RunsOnTheDefaultBrowser()
    {
        DefaultBrowserObserver.AssertSingle(_fixture.Context.Browser);
        await TestPages.ShowTitleAsync(_fixture.Page, GetType().Name);
    }
}

[Collection(nameof(MotusCollection))]
[Trait("TestCategory", "Integration")]
public class MotusCollectionAttributeTests1(BrowserContextFixture fixture) : MotusCollectionAttributeTestBase(fixture);

[Collection(nameof(MotusCollection))]
[Trait("TestCategory", "Integration")]
public class MotusCollectionAttributeTests2(BrowserContextFixture fixture) : MotusCollectionAttributeTestBase(fixture);

[Collection(nameof(MotusCollection))]
[Trait("TestCategory", "Integration")]
public class MotusCollectionAttributeTests3(BrowserContextFixture fixture) : MotusCollectionAttributeTestBase(fixture);

[Collection(nameof(MotusCollection))]
[Trait("TestCategory", "Integration")]
public class MotusCollectionAttributeTests4(BrowserContextFixture fixture) : MotusCollectionAttributeTestBase(fixture);

[Collection(nameof(MotusCollection))]
[Trait("TestCategory", "Integration")]
public class MotusCollectionAttributeTests5(BrowserContextFixture fixture) : MotusCollectionAttributeTestBase(fixture);

[Collection(nameof(MotusCollection))]
[Trait("TestCategory", "Integration")]
public class MotusCollectionAttributeTests6(BrowserContextFixture fixture) : MotusCollectionAttributeTestBase(fixture);
