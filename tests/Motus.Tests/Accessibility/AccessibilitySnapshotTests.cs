using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class AccessibilitySnapshotRecordTests
{
    [TestMethod]
    public void Record_HoldsProvidedValues()
    {
        var node = new AccessibilityNode(
            NodeId: "1",
            Role: "button",
            Name: "Submit",
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: 42);

        var snapshot = new AccessibilitySnapshot(
            Roots: [node],
            IgnoredCount: 3,
            DiagnosticMessage: "note");

        Assert.AreEqual(1, snapshot.Roots.Count);
        Assert.AreSame(node, snapshot.Roots[0]);
        Assert.AreEqual(3, snapshot.IgnoredCount);
        Assert.AreEqual("note", snapshot.DiagnosticMessage);
    }
}

[TestClass]
[TestCategory("Integration")]
public class AccessibilitySnapshotAccessorTests
{
    private IBrowser? _browser;

    [TestInitialize]
    public async Task Setup()
    {
        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    [TestMethod]
    public async Task AccessibilitySnapshotAsync_PreservesDeepDescendants()
    {
        var page = await _browser!.NewPageAsync();
        try
        {
            var html = "<main aria-label='Probe'><section aria-label='Outer'>"
                + "<article aria-label='Inner'><h1>Deep heading</h1>"
                + "<button>Deep button</button></article></section></main>";
            await page.GotoAsync("data:text/html," + Uri.EscapeDataString(html));

            var snapshot = await page.AccessibilitySnapshotAsync();
            var main = Walk(snapshot.Roots).Single(n => n.Role == "main" && n.Name == "Probe");
            var region = main.Children.Single(n => n.Role == "region" && n.Name == "Outer");
            var article = region.Children.Single(n => n.Role == "article" && n.Name == "Inner");

            Assert.IsTrue(article.Children.Any(n => n.Role == "heading" && n.Name == "Deep heading"));
            var button = article.Children.Single(n => n.Role == "button" && n.Name == "Deep button");
            Assert.IsNotNull(button.BackendDOMNodeId);
        }
        finally
        {
            await page.DisposeAsync();
        }
    }

    private static IEnumerable<AccessibilityNode> Walk(IEnumerable<AccessibilityNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var descendant in Walk(node.Children))
                yield return descendant;
        }
    }

    [TestMethod]
    public async Task AccessibilitySnapshotAsync_ReturnsRoots()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync("data:text/html,<button>Click me</button>");

        var snapshot = await page.AccessibilitySnapshotAsync();

        Assert.IsTrue(snapshot.Roots.Count > 0);
        Assert.IsTrue(snapshot.IgnoredCount >= 0);
        Assert.IsNull(snapshot.DiagnosticMessage);

        await page.DisposeAsync();
    }
}
