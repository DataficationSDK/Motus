namespace Motus.Samples.Tests;

/// <summary>
/// Accessibility testing showcase: page-level audits, rule skipping, locator AX queries,
/// and the .Not negation. Uses inline HTML with intentional WCAG violations.
/// </summary>
[TestClass]
public class AccessibilityTests : MotusTestBase
{
    /// <summary>
    /// Page with intentional accessibility violations:
    /// - Image without alt text (a11y-alt-text)
    /// - Empty button with no accessible name (a11y-empty-button)
    /// - Input without a label (a11y-unlabeled-form-control)
    /// - Properly labeled elements for positive assertions
    /// </summary>
    private const string AccessiblePage = """
        <!DOCTYPE html>
        <html lang="en">
        <head><title>Accessibility Sample</title></head>
        <body>
            <nav role="navigation" aria-label="Main">
                <a href="/home">Home</a>
                <a href="/about">About</a>
            </nav>
            <main>
                <h1>Accessibility Test Page</h1>

                <!-- Good: image with alt text -->
                <img src="logo.png" alt="Company logo" />

                <!-- Bad: image missing alt text -->
                <img src="hero.png" />

                <!-- Good: labeled button -->
                <button id="submit" type="submit">Submit order</button>

                <!-- Bad: empty button (no text, no aria-label) -->
                <button id="empty-btn"></button>

                <!-- Good: input with associated label -->
                <label for="email">Email address</label>
                <input id="email" type="email" placeholder="you@example.com" />

                <!-- Bad: input without any label -->
                <input id="search" type="text" placeholder="Search..." />

                <!-- Good: landmark with accessible name -->
                <footer role="contentinfo" aria-label="Site footer">
                    <p>Copyright 2026</p>
                </footer>
            </main>
        </body>
        </html>
        """;

    private const string CleanPage = """
        <!DOCTYPE html>
        <html lang="en">
        <head><title>Clean Page</title></head>
        <body>
            <main>
                <h1>Fully Accessible Page</h1>
                <img src="logo.png" alt="Logo" />
                <label for="name">Full name</label>
                <input id="name" type="text" />
                <button type="submit">Save</button>
            </main>
        </body>
        </html>
        """;

    [TestMethod]
    public async Task CleanPage_PassesAccessibilityAudit()
    {
        await Fixtures.SetPageContentAsync(Page, CleanPage);

        // A page with no violations should pass the audit
        await Expect.That(Page).ToPassAccessibilityAuditAsync();
    }

    [TestMethod]
    public async Task PageWithViolations_FailsAudit()
    {
        await Fixtures.SetPageContentAsync(Page, AccessiblePage);

        // The page has violations, so the negated assertion should pass
        await Expect.That(Page).Not.ToPassAccessibilityAuditAsync();
    }

    [TestMethod]
    public async Task SkipRules_AllowsPartialCompliance()
    {
        await Fixtures.SetPageContentAsync(Page, AccessiblePage);

        // Skip the rules we know this page violates to demonstrate filtering.
        // In practice, you'd use this to temporarily suppress known issues
        // while working toward full compliance.
        await Expect.That(Page).ToPassAccessibilityAuditAsync(opts =>
        {
            opts.SkipRules("a11y-alt-text", "a11y-empty-button", "a11y-unlabeled-form-control");
        });
    }

    [TestMethod]
    public async Task ExcludeWarnings_OnlyFailsOnErrors()
    {
        await Fixtures.SetPageContentAsync(Page, CleanPage);

        // With IncludeWarnings = false, only Error-severity violations cause failure
        await Expect.That(Page).ToPassAccessibilityAuditAsync(opts =>
        {
            opts.IncludeWarnings = false;
        });
    }

    [TestMethod]
    public async Task ToHaveAccessibleName_ChecksElementLabel()
    {
        await Fixtures.SetPageContentAsync(Page, AccessiblePage);

        // Assert that the submit button has the expected accessible name
        await Expect.That(Page.Locator("#submit")).ToHaveAccessibleNameAsync("Submit order");
    }

    [TestMethod]
    public async Task ToHaveRole_ChecksAriaRole()
    {
        await Fixtures.SetPageContentAsync(Page, AccessiblePage);

        // Assert the nav element has the navigation role
        await Expect.That(Page.Locator("nav")).ToHaveRoleAsync("navigation");
    }

    [TestMethod]
    public async Task ToHaveAccessibleName_Not_WhenNameDoesNotMatch()
    {
        await Fixtures.SetPageContentAsync(Page, AccessiblePage);

        // The empty button has no accessible name, so it should not match "Submit"
        await Expect.That(Page.Locator("#empty-btn")).Not.ToHaveAccessibleNameAsync("Submit");
    }
}
