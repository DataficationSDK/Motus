using Motus;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Snapshot;

[TestClass]
[TestCategory("Integration")]
public class PageSnapshotIntegrationTests
{
    private IBrowser? _browser;

    [TestInitialize]
    public async Task Setup()
    {
        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                ExecutablePath = ResolveInstalledBrowser(),
            });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration test.");
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    [TestMethod]
    public async Task TakeSnapshot_RealPage_ProducesRefs()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<button>Click me</button>");

        var service = new PageSnapshotService(page);
        var text = await service.TakeSnapshotAsync();

        StringAssert.Contains(text, "button");
        StringAssert.Contains(text, "[ref=e");
    }

    [TestMethod]
    public async Task RefMap_StableAcrossResnapshot_OfUnchangedPage()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<button>A</button><button>B</button>");

        var service = new PageSnapshotService(page);
        var first = await service.TakeSnapshotAsync();
        var second = await service.TakeSnapshotAsync();

        Assert.AreEqual(first, second);
    }

    [TestMethod]
    public async Task ResolveRef_ResolvesToExpectedElement()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<button>Go</button>");

        var service = new PageSnapshotService(page);
        var text = await service.TakeSnapshotAsync();
        var refId = ExtractRefForLineContaining(text, "Go");

        var locator = service.ResolveRef(refId);
        var content = await locator.TextContentAsync();

        Assert.AreEqual("Go", content);
    }

    [TestMethod]
    public async Task ResolveRef_DetachedNode_Throws()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<button id=\"b\">Go</button>");

        var service = new PageSnapshotService(page);
        var text = await service.TakeSnapshotAsync();
        var refId = ExtractRefForLineContaining(text, "Go");
        var locator = service.ResolveRef(refId);

        await page.EvaluateAsync<object>("document.getElementById('b').remove()");

        Exception? caught = null;
        try
        {
            await locator.ClickAsync(timeout: 1000);
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        Assert.IsNotNull(caught, "Acting on a detached element should fail.");
    }

    private static string ExtractRefForLineContaining(string snapshot, string needle)
    {
        foreach (var line in snapshot.Split('\n'))
        {
            if (!line.Contains(needle, StringComparison.Ordinal))
                continue;

            var marker = line.IndexOf("[ref=", StringComparison.Ordinal);
            if (marker < 0)
                continue;

            var start = marker + "[ref=".Length;
            var end = line.IndexOf(']', start);
            return line[start..end];
        }

        throw new AssertFailedException($"No ref found on a line containing '{needle}'.");
    }

    private static string? ResolveInstalledBrowser()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".motus",
            "browsers");

        foreach (var marker in new[] { ".installed.chromium", ".installed" })
        {
            var markerPath = Path.Combine(cacheDir, marker);
            if (!File.Exists(markerPath))
                continue;

            var executablePath = File.ReadAllText(markerPath).Trim();
            if (File.Exists(executablePath))
                return executablePath;
        }

        return null;
    }
}
