namespace Motus.Samples.Tests;

/// <summary>
/// Selector maintenance showcase.
///
/// This file is intended as a TARGET for the <c>motus check-selectors</c> CLI tool.
/// The tests themselves run normally with <c>dotnet test</c>; their value as a
/// showcase is the mix of selector kinds they contain.
///
/// Try the following invocations from the repository root:
///
///   # 1. Validate every selector in this file against a live data: page (no manifest).
///   #    Reports each selector as pass / broken / ambiguous.
///   motus check-selectors \
///       "tools/Motus/samples/Motus.Samples/Tests/SelectorMaintenanceShowcaseTests.cs" \
///       --base-url "data:text/html,&lt;html&gt;&lt;body&gt;&lt;h1 data-testid='main-heading'&gt;Dashboard&lt;/h1&gt;&lt;/body&gt;&lt;/html&gt;"
///
///   # 2. Same as above, but exit non-zero if anything is broken (CI mode):
///   motus check-selectors "...SelectorMaintenanceShowcaseTests.cs" --base-url "..." --ci
///
///   # 3. Apply High-confidence repairs automatically. Requires a manifest, which is
///   #    normally produced by `motus record` or `motus codegen` and lives next to
///   #    the test file as <c>SelectorMaintenanceShowcaseTests.selectors.json</c>:
///   motus check-selectors "...SelectorMaintenanceShowcaseTests.cs" \
///       --manifest "...SelectorMaintenanceShowcaseTests.selectors.json" --fix
///
///   # 4. Open the visual runner to review and apply repairs interactively.
///   #    For each broken selector you see the live page, the ranked suggestions,
///   #    and accept / edit / skip controls. Accepted choices are written to source.
///   motus check-selectors "...SelectorMaintenanceShowcaseTests.cs" \
///       --manifest "...SelectorMaintenanceShowcaseTests.selectors.json" --interactive
///
/// The selectors below are intentionally chosen to show the full ladder of stability:
///   - <c>GetByTestId</c>      → most stable; usually passes the health check
///   - <c>GetByRole</c>        → stable as long as ARIA roles do not change
///   - <c>GetByText</c>        → fragile to copy edits
///   - <c>Locator(css)</c>     → most fragile; class renames break this immediately
/// </summary>
[TestClass]
public class SelectorMaintenanceShowcaseTests : MotusTestBase
{
    [TestMethod]
    public async Task StableSelectors_ResolveAgainstDashboard()
    {
        await Fixtures.SetPageContentAsync(Page, Fixtures.Dashboard);

        // data-testid is the most stable strategy. The selector survives DOM
        // restructuring, class renames, and copy edits as long as the attribute
        // stays on the element.
        var heading = Page.GetByTestId("main-heading");
        await Expect.That(heading).ToContainTextAsync("Dashboard");

        // ARIA role + accessible name. Stable across visual redesigns; breaks
        // only when the role or label semantics change.
        var revenueCard = Page.GetByTestId("card-revenue");
        await Expect.That(revenueCard).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task FragileSelectors_DemonstrateWhatCheckSelectorsCatches()
    {
        await Fixtures.SetPageContentAsync(Page, Fixtures.Dashboard);

        // A CSS selector tied to class names. If the design system renames `.cards`
        // or `.card`, every test that uses these locators silently breaks. Running
        // `motus check-selectors --interactive` highlights the moved element on the
        // live page and proposes a stable replacement (typically the corresponding
        // data-testid) so the fix can be applied without leaving the tool.
        var cards = Page.Locator(".cards .card");
        await Expect.That(cards).ToHaveCountAsync(3);

        // Visible-text matching is fragile to copy edits. A product manager renaming
        // "Users" to "Members" would break this — `motus check-selectors` reports
        // it as broken and the repair pipeline suggests `GetByTestId("card-users")`
        // as a stable replacement (the fingerprint locates the moved element by
        // its data-testid and ancestor path, even when the visible text changed).
        var usersCard = Page.GetByText("Users", exact: true);
        await Expect.That(usersCard).ToBeAttachedAsync();
    }
}
