using System.Collections.Concurrent;
using Motus.Abstractions;
using Xunit;

namespace Motus.Testing.xUnit.Tests;

// A context per test rather than per class: the test class creates the fixture and drives it from
// its own IAsyncLifetime. Six rows is more than the four contexts allowed open at once, so each
// row has to give its slot back for the next to start.

[Trait("TestCategory", "Integration")]
public class PerTestDefaultContextTests : IAsyncLifetime
{
    private static readonly ConcurrentDictionary<IBrowserContext, byte> s_contexts = new(ReferenceEqualityComparer.Instance);

    private readonly BrowserContextFixture _ctx = new();

    public Task InitializeAsync() => _ctx.InitializeAsync();
    public Task DisposeAsync() => _ctx.DisposeAsync();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task EachTestGetsItsOwnContext(int row)
    {
        Assert.True(s_contexts.TryAdd(_ctx.Context, 0), "A context was handed to two tests.");
        DefaultBrowserObserver.AssertSingle(_ctx.Context.Browser);
        await TestPages.ShowTitleAsync(_ctx.Page, $"PerTest{row}");
    }
}

[Collection(PerCollectionBrowser.Name)]
[Trait("TestCategory", "Integration")]
public class PerTestCollectionContextTests : IAsyncLifetime
{
    private static readonly ConcurrentDictionary<IBrowserContext, byte> s_contexts = new(ReferenceEqualityComparer.Instance);

    private readonly SharedBrowserFixture _browser;
    private readonly BrowserContextFixture<SharedBrowserFixture> _ctx;

    public PerTestCollectionContextTests(SharedBrowserFixture browser)
    {
        _browser = browser;
        _ctx = new BrowserContextFixture<SharedBrowserFixture>(browser);
    }

    public Task InitializeAsync() => _ctx.InitializeAsync();
    public Task DisposeAsync() => _ctx.DisposeAsync();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task EachTestGetsItsOwnContextFromTheCollectionBrowser(int row)
    {
        Assert.True(s_contexts.TryAdd(_ctx.Context, 0), "A context was handed to two tests.");
        Assert.Same(_browser.Browser, _ctx.Context.Browser);
        await TestPages.ShowTitleAsync(_ctx.Page, $"PerCollectionTest{row}");
    }
}
