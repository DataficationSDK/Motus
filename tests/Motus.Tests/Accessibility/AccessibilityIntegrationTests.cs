using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class AccessibilityIntegrationTests
{
    private static AccessibilityNode BuildNode(
        string nodeId,
        string role,
        string? name = null,
        long? backendNodeId = null,
        Dictionary<string, string?>? props = null) =>
        new(
            NodeId: nodeId,
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: props ?? new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: backendNodeId);

    [TestMethod]
    public void FullRuleSet_AgainstSyntheticTree_DetectsExpectedViolations()
    {
        // Build a synthetic tree with known violations
        var nodes = new List<AccessibilityNode>
        {
            // First node (page-level rules trigger here)
            BuildNode("1", "RootWebArea", "Test Page"),

            // Missing alt text on image
            BuildNode("2", "img", name: null, backendNodeId: 10),

            // Unlabeled form control
            BuildNode("3", "textbox", name: null, backendNodeId: 11),

            // Valid button
            BuildNode("4", "button", name: "Submit"),

            // Empty button (violation)
            BuildNode("5", "button", name: null, backendNodeId: 12),

            // Empty link (violation)
            BuildNode("6", "link", name: null, backendNodeId: 13),

            // Valid heading h1
            BuildNode("7", "heading", name: "Title",
                props: new Dictionary<string, string?> { ["level"] = "1" }),

            // Skipped heading h3 (violation)
            BuildNode("8", "heading", name: "Subtitle",
                backendNodeId: 14,
                props: new Dictionary<string, string?> { ["level"] = "3" }),

            // No main landmark (violation - detected on first node)
        };

        // Pre-fetched data: no duplicate IDs, no document language, no computed styles
        var context = new AccessibilityAuditContext(
            AllNodes: nodes,
            Page: null!,
            ComputedStyles: new Dictionary<long, ComputedStyleInfo>(),
            DuplicateIds: new HashSet<string> { "nav" }, // one duplicate ID
            DocumentLanguage: null); // missing lang

        // Register all rules
        var rules = new IAccessibilityRule[]
        {
            new AltTextAccessibilityRule(),
            new UnlabeledFormControlRule(),
            new MissingLandmarkRule(),
            new HeadingHierarchyRule(),
            new EmptyButtonRule(),
            new EmptyLinkRule(),
            new ColorContrastRule(),
            new DuplicateIdRule(),
            new MissingDocumentLanguageRule()
        };

        var engine = new AccessibilityRuleEngine(rules);
        var result = engine.Run(nodes, context);

        // Expected violations:
        // 1. a11y-alt-text (img without name)
        // 2. a11y-unlabeled-form-control (textbox without name)
        // 3. a11y-empty-button (button without name)
        // 4. a11y-empty-link (link without name)
        // 5. a11y-heading-hierarchy (h1 to h3 skip)
        // 6. a11y-missing-landmark (no main)
        // 7. a11y-duplicate-id (nav appears twice)
        // 8. a11y-missing-lang (no document language)

        var ruleIds = result.Violations.Select(v => v.RuleId).ToHashSet();

        Assert.IsTrue(ruleIds.Contains("a11y-alt-text"), "Expected alt-text violation");
        Assert.IsTrue(ruleIds.Contains("a11y-unlabeled-form-control"), "Expected unlabeled form control violation");
        Assert.IsTrue(ruleIds.Contains("a11y-empty-button"), "Expected empty button violation");
        Assert.IsTrue(ruleIds.Contains("a11y-empty-link"), "Expected empty link violation");
        Assert.IsTrue(ruleIds.Contains("a11y-heading-hierarchy"), "Expected heading hierarchy violation");
        Assert.IsTrue(ruleIds.Contains("a11y-missing-landmark"), "Expected missing landmark violation");
        Assert.IsTrue(ruleIds.Contains("a11y-duplicate-id"), "Expected duplicate ID violation");
        Assert.IsTrue(ruleIds.Contains("a11y-missing-lang"), "Expected missing lang violation");

        Assert.AreEqual(8, result.ViolationCount, $"Expected 8 violations, got {result.ViolationCount}. Violations: {string.Join(", ", result.Violations.Select(v => v.RuleId))}");
    }

    [TestMethod]
    public void FullRuleSet_CleanPage_NoViolations()
    {
        var nodes = new List<AccessibilityNode>
        {
            BuildNode("1", "RootWebArea", "Test Page"),
            BuildNode("2", "main", "Main content"),
            BuildNode("3", "img", name: "Company logo"),
            BuildNode("4", "textbox", name: "Email"),
            BuildNode("5", "button", name: "Submit"),
            BuildNode("6", "link", name: "Home"),
            BuildNode("7", "heading", name: "Welcome",
                props: new Dictionary<string, string?> { ["level"] = "1" }),
            BuildNode("8", "heading", name: "Details",
                props: new Dictionary<string, string?> { ["level"] = "2" }),
        };

        var context = new AccessibilityAuditContext(
            AllNodes: nodes,
            Page: null!,
            ComputedStyles: new Dictionary<long, ComputedStyleInfo>(),
            DuplicateIds: new HashSet<string>(),
            DocumentLanguage: "en");

        var rules = new IAccessibilityRule[]
        {
            new AltTextAccessibilityRule(),
            new UnlabeledFormControlRule(),
            new MissingLandmarkRule(),
            new HeadingHierarchyRule(),
            new EmptyButtonRule(),
            new EmptyLinkRule(),
            new ColorContrastRule(),
            new DuplicateIdRule(),
            new MissingDocumentLanguageRule()
        };

        var engine = new AccessibilityRuleEngine(rules);
        var result = engine.Run(nodes, context);

        Assert.AreEqual(0, result.ViolationCount,
            $"Expected 0 violations but got: {string.Join(", ", result.Violations.Select(v => $"{v.RuleId}: {v.Message}"))}");
    }
}

/// <summary>
/// Runs the built-in rules against a real page, which is the only way to catch a rule that
/// matches a role name no browser ever emits. Hand-built nodes cannot: they carry whatever role
/// the test wrote.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class AccessibilityAuditBrowserTests
{
    // A 1x1 transparent gif, inline so the audit needs no network.
    private const string PixelSrc =
        "data:image/gif;base64,R0lGODlhAQABAAAAACH5BAEAAAAALAAAAAABAAEAAAIBAAA=";

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
    public async Task Audit_ImageWithoutAltText_FiresAltTextRule()
    {
        var html = "<html lang='en'><body><main>"
            + $"<img src='{PixelSrc}'>"
            + $"<img alt='Company logo' src='{PixelSrc}'>"
            + $"<img alt='' src='{PixelSrc}'>"
            + "</main></body></html>";

        var page = await _browser!.NewPageAsync();
        try
        {
            await page.GotoAsync("data:text/html," + Uri.EscapeDataString(html));

            var result = await page.RunAccessibilityAuditAsync();
            var altText = result.Violations.Where(v => v.RuleId == "a11y-alt-text").ToList();

            // Only the first image is at fault: the second is named, and the third opts out of
            // the tree entirely with an empty alt.
            Assert.AreEqual(1, altText.Count,
                "expected one alt text violation, got: "
                + string.Join(", ", result.Violations.Select(v => $"{v.RuleId}(role={v.NodeRole})")));
            Assert.AreEqual(AccessibilityViolationSeverity.Error, altText[0].Severity);
            Assert.IsNotNull(altText[0].BackendDOMNodeId);
            Assert.IsTrue(string.IsNullOrWhiteSpace(altText[0].NodeName));

            // The role a violation reports is the one the tree gave, so an audit and a snapshot
            // name the same node the same way.
            var snapshot = await page.AccessibilitySnapshotAsync();
            var reported = Walk(snapshot.Roots)
                .Single(n => n.BackendDOMNodeId == altText[0].BackendDOMNodeId);
            Assert.AreEqual(reported.Role, altText[0].NodeRole);
        }
        finally
        {
            await page.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Audit_ExplicitImgRoleWithoutName_FiresAltTextRule()
    {
        // An author-supplied role="img" reaches the tree under the browser's own spelling too,
        // so it is not a way around the mismatch this test guards.
        var html = "<html lang='en'><body><main>"
            + "<svg role='img' width='10' height='10'><rect width='10' height='10'/></svg>"
            + "</main></body></html>";

        var page = await _browser!.NewPageAsync();
        try
        {
            await page.GotoAsync("data:text/html," + Uri.EscapeDataString(html));

            var result = await page.RunAccessibilityAuditAsync();

            Assert.IsTrue(
                result.Violations.Any(v => v.RuleId == "a11y-alt-text"),
                "expected an alt text violation, got: "
                + string.Join(", ", result.Violations.Select(v => $"{v.RuleId}(role={v.NodeRole})")));
        }
        finally
        {
            await page.DisposeAsync();
        }
    }

    [TestMethod]
    public async Task Audit_NamedImages_DoNotFireAltTextRule()
    {
        var html = "<html lang='en'><body><main>"
            + $"<img alt='Company logo' src='{PixelSrc}'>"
            + $"<img aria-label='Chart of sales' src='{PixelSrc}'>"
            + $"<img alt='' src='{PixelSrc}'>"
            + $"<img aria-hidden='true' src='{PixelSrc}'>"
            + "</main></body></html>";

        var page = await _browser!.NewPageAsync();
        try
        {
            await page.GotoAsync("data:text/html," + Uri.EscapeDataString(html));

            var result = await page.RunAccessibilityAuditAsync();

            Assert.IsFalse(
                result.Violations.Any(v => v.RuleId == "a11y-alt-text"),
                "no image on this page is unnamed, got: "
                + string.Join(", ", result.Violations.Select(v => $"{v.RuleId}(role={v.NodeRole})")));
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
}
