namespace Motus.Samples.Tests;

/// <summary>
/// Navigation, locator factories, and locator chaining.
/// Demonstrates every built-in locator strategy and how to compose them.
/// </summary>
[TestClass]
public class NavigationAndLocatorsTests : MotusTestBase
{
    [TestMethod]
    public async Task SetContent_TitleIsAccessible()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // TitleAsync reads the <title> element
        var title = await Page.TitleAsync();
        Assert.AreEqual("Dashboard", title);

        // Expect.That(IPage) provides auto-retrying page-level assertions
        await Expect.That(Page).ToHaveTitleAsync("Dashboard");
    }

    [TestMethod]
    public async Task GetByRole_FindsButton()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.TodoApp);

        // GetByRole matches ARIA roles; the second parameter filters by accessible name
        var addButton = Page.GetByRole("button", "Add");
        await Expect.That(addButton).ToBeVisibleAsync();
    }

    [TestMethod]
    public async Task GetByLabel_FindsInput()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.LoginForm);

        // GetByLabel matches by associated <label> text
        var emailInput = Page.GetByLabel("Email");
        await Expect.That(emailInput).ToBeEnabledAsync();
    }

    [TestMethod]
    public async Task GetByPlaceholder_FindsInput()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.LoginForm);

        // GetByPlaceholder matches the placeholder attribute
        var input = Page.GetByPlaceholder("you@example.com");
        await Expect.That(input).ToBeEditableAsync();
    }

    [TestMethod]
    public async Task GetByTestId_FindsElement()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // GetByTestId matches data-testid attributes
        var card = Page.GetByTestId("card-revenue");
        await Expect.That(card).ToBeAttachedAsync();
    }

    [TestMethod]
    public async Task GetByText_FindsHeading()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // GetByText matches visible text content
        var heading = Page.GetByText("Dashboard", exact: true);
        await Expect.That(heading).ToContainTextAsync("Dashboard");
    }

    [TestMethod]
    public async Task LocatorChaining_NthAndFilter()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // Locator returns all matches; Filter narrows by text; Nth picks one by index
        var cards = Page.Locator(".card");
        await Expect.That(cards).ToHaveCountAsync(3);

        // Filter narrows by text content; First gets the single match
        var revenueCard = cards.Filter(new LocatorOptions { HasText = "Revenue" }).First;
        await Expect.That(revenueCard).ToContainTextAsync("$12,345");

        // Nth is zero-based
        var secondCard = cards.Nth(1);
        await Expect.That(secondCard).ToContainTextAsync("Users");
    }
}
