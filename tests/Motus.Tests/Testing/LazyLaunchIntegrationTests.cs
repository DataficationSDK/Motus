using Motus.Abstractions;
using Motus.Testing.MSTest;

namespace Motus.Tests.Testing;

/// <summary>
/// Runs <see cref="MotusTestBase"/> the way a new project does: no <c>[AssemblyInitialize]</c>
/// anywhere in the assembly, so the shared browser has to come up on first use.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class LazyLaunchIntegrationTests : MotusTestBase
{
    private static IBrowser? s_firstBrowser;

    [TestMethod]
    public async Task FirstTest_GetsAPageWithoutAssemblySetup()
        => await AssertUsableAndSharedAsync("first");

    [TestMethod]
    public async Task SecondTest_SharesTheSameBrowser()
        => await AssertUsableAndSharedAsync("second");

    private async Task AssertUsableAndSharedAsync(string text)
    {
        await Page.GotoAsync($"data:text/html,<p id=t>{text}</p>");
        Assert.AreEqual(text, await Page.Locator("#t").TextContentAsync());

        var first = Interlocked.CompareExchange(ref s_firstBrowser, Context.Browser, null) ?? Context.Browser;
        Assert.AreSame(first, Context.Browser, "every test without an override should share one browser");
    }
}

/// <summary>
/// A class that overrides <see cref="MotusTestBase.LaunchOptions"/> gets a browser launched with
/// those options, rather than having them ignored in favor of the shared browser.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class LaunchOptionsOverrideIntegrationTests : MotusTestBase
{
    private const string UserAgent = "MotusLaunchOptionsOverride/1.0";

    protected override LaunchOptions? LaunchOptions => new()
    {
        Args = [$"--user-agent={UserAgent}"],
    };

    [TestMethod]
    public async Task Override_LaunchesABrowserWithThoseOptions()
    {
        await Page.GotoAsync("data:text/html,<p>override</p>");

        Assert.AreEqual(UserAgent, await Page.EvaluateAsync<string>("navigator.userAgent"));
        Assert.AreNotSame(TryGetSharedBrowser(), Context.Browser);
    }

    private static IBrowser? TryGetSharedBrowser()
    {
        try { return SharedFixture.Browser; }
        catch (InvalidOperationException) { return null; }
    }
}
