using Motus.Abstractions;

namespace Motus.Tests.Browser;

/// <summary>
/// Pins the switches that stop Chromium from backgrounding the pages Motus drives, and that each
/// of them can still be taken away.
/// </summary>
/// <remarks>
/// Without them a page whose window the browser thinks is covered is marked hidden, and its timers,
/// animation frames and observers stop. Opening and closing a second context was enough to do that
/// to a page in the first one.
/// </remarks>
[TestClass]
public class ChromiumBackgroundingArgsTests
{
    private static readonly string[] BackgroundingSwitches =
    [
        "--disable-backgrounding-occluded-windows",
        "--disable-renderer-backgrounding",
        "--disable-background-timer-throttling",
    ];

    [TestMethod]
    public void Build_IncludesTheBackgroundingSwitchesByDefault()
    {
        var args = ChromiumArgs.Build(new LaunchOptions(), "/tmp/profile");

        foreach (var flag in BackgroundingSwitches)
            CollectionAssert.Contains(args, flag);
    }

    [TestMethod]
    public void Build_IncludesTheBackgroundingSwitchesHeadedToo()
    {
        var args = ChromiumArgs.Build(new LaunchOptions { Headless = false }, "/tmp/profile");

        foreach (var flag in BackgroundingSwitches)
            CollectionAssert.Contains(args, flag);
    }

    [TestMethod]
    [DataRow("--disable-backgrounding-occluded-windows")]
    [DataRow("--disable-renderer-backgrounding")]
    [DataRow("--disable-background-timer-throttling")]
    public void Build_IgnoreDefaultArgs_RemovesOneBackgroundingSwitchAndKeepsTheOthers(string ignored)
    {
        var args = ChromiumArgs.Build(new LaunchOptions { IgnoreDefaultArgs = [ignored] }, "/tmp/profile");

        CollectionAssert.DoesNotContain(args, ignored);
        foreach (var flag in BackgroundingSwitches.Where(f => f != ignored))
            CollectionAssert.Contains(args, flag);
    }

    [TestMethod]
    public void Build_IgnoringEveryDefault_LeavesNoneOfThem()
    {
        var options = new LaunchOptions { IgnoreDefaultArgs = ChromiumArgs.DefaultArgs };

        var args = ChromiumArgs.Build(options, "/tmp/profile");

        foreach (var flag in ChromiumArgs.DefaultArgs)
            CollectionAssert.DoesNotContain(args, flag);

        CollectionAssert.Contains(args, "--remote-debugging-pipe", "the transport switch is not a default to ignore");
        CollectionAssert.Contains(args, "--user-data-dir=/tmp/profile", "the profile switch is not a default to ignore");
    }

    [TestMethod]
    public void Build_NoSwitchIsGivenTwice()
    {
        var args = ChromiumArgs.Build(new LaunchOptions(), "/tmp/profile");

        var names = args.Select(a => a.Split('=', 2)[0]).ToList();
        CollectionAssert.AllItemsAreUnique(names);
    }

    /// <summary>
    /// Chromium honors only the last of a repeated feature-list switch. A default
    /// <c>--disable-features</c> or <c>--enable-features</c> would therefore be silently replaced
    /// by one the caller passes, or replace it, so the defaults leave both lists to the caller.
    /// </summary>
    [TestMethod]
    public void Build_LeavesFeatureListsToTheCaller()
    {
        var options = new LaunchOptions { Args = ["--disable-features=Translate"] };

        var args = ChromiumArgs.Build(options, "/tmp/profile");

        Assert.AreEqual(1, args.Count(a => a.StartsWith("--disable-features", StringComparison.Ordinal)));
        Assert.AreEqual(0, args.Count(a => a.StartsWith("--enable-features", StringComparison.Ordinal)));
        CollectionAssert.Contains(args, "--disable-features=Translate");
    }
}
