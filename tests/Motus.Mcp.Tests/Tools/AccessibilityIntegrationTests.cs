using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the accessibility tool through a real browser: auditing a page seeded with
/// known defects, confirming the violations come back with refs for the addressable
/// ones, and that the severity filter narrows the result.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class AccessibilityIntegrationTests
{
    // A page with an image missing alt text, an empty button, an unlabeled input, and
    // (being a data: document) no lang attribute. Each trips a built-in rule.
    private const string DefectivePage =
        "data:text/html,<title>A11y</title><img src=x><button></button><input>";

    private BrowserSessionManager? _sessions;
    private ActivePageService? _pages;

    [TestInitialize]
    public void Setup()
    {
        var executablePath = ResolveInstalledBrowser();
        if (executablePath is null)
            Assert.Inconclusive("No installed browser found; skipping integration test.");

        _sessions = new BrowserSessionManager(new McpServerLaunchOptions
        {
            Headless = true,
            ExecutablePath = executablePath,
        });
        _pages = new ActivePageService(_sessions);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_pages is not null)
            await _pages.DisposeAsync();
        if (_sessions is not null)
            await _sessions.DisposeAsync();
    }

    [TestMethod]
    public async Task AuditDefectivePage_OnARealBrowser()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        AssertOk(await CoreTools.NavigateAsync(DefectivePage, pages, ct), "navigate");

        var all = await AccessibilityTools.AuditAccessibilityAsync(null, pages, ct);
        AssertOk(all, "audit_accessibility");

        var violations = Violations(all);
        Assert.IsTrue(violations.Length > 0, "expected at least one violation");

        var rules = violations.Select(v => v.GetProperty("ruleId").GetString()).ToArray();
        var rulesText = string.Join(", ", rules);

        // A data: document carries no lang attribute, so this page-level rule fires.
        CollectionAssert.Contains(rules, "a11y-missing-lang", $"rules were: {rulesText}");

        // The empty button and unlabeled input are addressable, so at least one
        // violation carries a ref back to its element.
        Assert.IsTrue(
            violations.Any(v => v.GetProperty("ref").ValueKind != JsonValueKind.Null),
            $"expected at least one violation to carry a ref; rules were: {rulesText}");

        // Filtering to errors returns a subset, all of which are errors.
        var errorsOnly = await AccessibilityTools.AuditAccessibilityAsync("error", pages, ct);
        AssertOk(errorsOnly, "audit_accessibility error filter");

        var errors = Violations(errorsOnly);
        Assert.IsTrue(errors.Length <= violations.Length);
        Assert.IsTrue(
            errors.All(v => v.GetProperty("severity").GetString() == "Error"),
            "every filtered violation should be an error");
    }

    private static JsonElement[] Violations(CallToolResult result)
    {
        Assert.IsNotNull(result.StructuredContent, "expected structured content");
        return result.StructuredContent!.Value.GetProperty("violations").EnumerateArray().ToArray();
    }

    private static void AssertOk(CallToolResult result, string label)
        => Assert.IsFalse(result.IsError ?? false, $"{label} should succeed: {TextOf(result)}");

    private static string TextOf(CallToolResult result)
        => result.Content.Count > 0 && result.Content[0] is TextContentBlock t ? t.Text : string.Empty;

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
