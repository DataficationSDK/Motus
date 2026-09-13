using Motus;
using Motus.Abstractions;
using Motus.Mcp;
using Motus.Mcp.Tests.Fixtures;

namespace Motus.Mcp.Tests.Snapshot;

/// <summary>
/// Reads the bench page in a real browser and checks that the snapshot puts content where the
/// markup puts it. The page nests a form inside five layout divs and each list item's text inside
/// two more, which is the shape that decides whether an agent can tell which control belongs to
/// which form.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class McpBenchSnapshotIntegrationTests
{
    private IBrowser? _browser;
    private McpBenchFixtureServer? _server;

    [TestInitialize]
    public async Task Setup()
    {
        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration test.");
        }

        _server = new McpBenchFixtureServer();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _server?.Dispose();
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    [TestMethod]
    public async Task Snapshot_BenchPage_KeepsContentWhereTheMarkupPutIt()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(_server!.IndexUrl);

        var snapshot = await page.AccessibilitySnapshotAsync();

        var root = snapshot.Roots.Single();
        Assert.AreEqual("RootWebArea", root.Role, "the document is the only root of the tree");

        var toEmail = PathTo(root, n => n.Role == "textbox" && n.Name == "Email")
            ?? throw new AssertFailedException("The email field is not in the snapshot.");
        Assert.IsTrue(
            toEmail.Any(n => n.Role == "main"),
            "the email field sits inside main: " + string.Join(" > ", toEmail.Select(Describe)));

        var list = Walk(root).First(n => n.Role == "list");
        var firstItem = list.Children.First(n => n.Role == "listitem");
        Assert.IsTrue(
            Walk(firstItem).Any(n => n.Name == "Item one"),
            "the first list item carries its own text: " + string.Join(", ", firstItem.Children.Select(Describe)));
    }

    [TestMethod]
    public async Task Snapshot_BenchPage_RefsAddressTheFormControls()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(_server!.IndexUrl);

        var service = new PageSnapshotService(page);
        var text = await service.TakeSnapshotAsync();

        StringAssert.Contains(text, "textbox \"Email\"");
        StringAssert.Contains(text, "button \"Submit\"");
        Assert.IsFalse(
            text.Contains("no addressable elements"),
            "a page full of controls should not carry the degraded note");
    }

    [TestMethod]
    public async Task Snapshot_BenchPage_IsCompact_AndCarriesTheUsefulAttributes()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(_server!.IndexUrl);

        var service = new PageSnapshotService(page);
        var text = await service.TakeSnapshotAsync();

        // Text is folded into the element it names or printed inline, never as layout nodes.
        Assert.IsFalse(text.Contains("InlineTextBox"), text);
        Assert.IsFalse(text.Contains("StaticText"), text);
        Assert.IsFalse(text.Contains("ListMarker"), text);
        StringAssert.Contains(text, "- listitem: Item one\n");
        StringAssert.Contains(text, "- LabelText: Email\n");

        StringAssert.Contains(text, "- heading \"Checkout\" [ref=e4] [level=1]\n");
        StringAssert.Contains(text, $"- link \"Other page\" [ref=e2] [url={_server.OtherUrl}]\n");
        StringAssert.Contains(text, "- combobox \"Plan\" [ref=e6] [value=\"Free\"]\n");
        StringAssert.Contains(text, "- Iframe \"Payment frame\" [ref=e14]\n");

        // Refs go to controls, named nodes, and frames; wrappers and labels get none.
        Assert.AreEqual(14, service.LastSnapshot!.Split("[ref=").Length - 1, text);
        Assert.IsFalse(text.Contains("- form ["), "an unnamed form is not a target: " + text);
        Assert.IsFalse(text.Contains("- LabelText ["), "a label is not a target: " + text);
    }

    private static string Describe(AccessibilityNode node)
        => string.IsNullOrEmpty(node.Name) ? node.Role ?? "generic" : $"{node.Role} \"{node.Name}\"";

    /// <summary>
    /// The chain of nodes from the root down to the first node matching the predicate, inclusive,
    /// or null when no node matches.
    /// </summary>
    private static IReadOnlyList<AccessibilityNode>? PathTo(
        AccessibilityNode node, Func<AccessibilityNode, bool> match)
    {
        if (match(node))
            return [node];

        foreach (var child in node.Children)
        {
            if (PathTo(child, match) is { } path)
                return [node, .. path];
        }

        return null;
    }

    private static IEnumerable<AccessibilityNode> Walk(AccessibilityNode node)
    {
        yield return node;
        foreach (var child in node.Children)
        {
            foreach (var descendant in Walk(child))
                yield return descendant;
        }
    }
}
