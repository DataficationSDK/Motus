using System.Collections.Concurrent;
using Motus.Abstractions;
using Xunit;

namespace Motus.Testing.xUnit.Tests;

internal static class TestPages
{
    /// <summary>
    /// Navigates to a page with the given title and checks the browser reports it back.
    /// </summary>
    internal static async Task ShowTitleAsync(IPage page, string title)
    {
        await page.GotoAsync($"data:text/html,<title>{title}</title>");
        Assert.Equal(title, await page.TitleAsync());
    }
}

/// <summary>
/// Records the browser behind every context that came from the browser the adapter shares across
/// the test process, so tests can check that only one was ever launched.
/// </summary>
internal static class DefaultBrowserObserver
{
    private static readonly ConcurrentDictionary<IBrowser, byte> s_seen = new(ReferenceEqualityComparer.Instance);

    internal static void AssertSingle(IBrowser browser)
    {
        s_seen.TryAdd(browser, 0);
        Assert.Single(s_seen);
    }
}
