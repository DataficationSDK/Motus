namespace Motus.Samples.Tests;

/// <summary>
/// Phase 2B: Assertion showcase covering Expect.That, .Not, custom options, and various matchers.
/// </summary>
[TestClass]
public class AssertionsShowcaseTests : MotusTestBase
{
    [TestMethod]
    public async Task ToBeVisible_AndHidden()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        var sidebar = Page.Locator("#sidebar");
        await Expect.That(sidebar).ToBeVisibleAsync();

        // Toggle sidebar off
        await Page.Locator("#toggle-sidebar").ClickAsync();
        await Expect.That(sidebar).ToBeHiddenAsync();
    }

    [TestMethod]
    public async Task Not_Inversion()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // .Not inverts the assertion: the sidebar starts visible, so Not.ToBeHiddenAsync passes
        await Expect.That(Page.Locator("#sidebar")).Not.ToBeHiddenAsync();
    }

    [TestMethod]
    public async Task ToHaveText_ExactMatch()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.TodoApp);

        // ToHaveTextAsync checks the full text content of the element
        await Expect.That(Page.GetByTestId("active-count")).ToHaveTextAsync("0 items left");
    }

    [TestMethod]
    public async Task ToContainText_PartialMatch()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // ToContainTextAsync passes when the element's text includes the substring
        await Expect.That(Page.GetByTestId("card-revenue")).ToContainTextAsync("12,345");
    }

    [TestMethod]
    public async Task ToHaveAttribute_ChecksHref()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // ToHaveAttributeAsync checks any attribute's value
        var homeLink = Page.GetByTestId("nav-home");
        await Expect.That(homeLink).ToHaveAttributeAsync("href", "/home");
    }

    [TestMethod]
    public async Task ToHaveCount_AfterDynamicRender()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.TodoApp);

        // Start with zero items
        await Expect.That(Page.Locator(".todo-item")).ToHaveCountAsync(0);

        // Add items dynamically
        var input = Page.GetByPlaceholder("What needs to be done?");
        var addBtn = Page.GetByRole("button", "Add");

        await input.FillAsync("First");
        await addBtn.ClickAsync();
        await input.FillAsync("Second");
        await addBtn.ClickAsync();

        // ToHaveCountAsync auto-retries until the expected count is reached
        await Expect.That(Page.Locator(".todo-item")).ToHaveCountAsync(2);
    }

    [TestMethod]
    public async Task CustomAssertionOptions_TimeoutAndMessage()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.Dashboard);

        // AssertionOptions lets you customize timeout and the failure message
        await Expect.That(Page.GetByText("Dashboard")).ToBeVisibleAsync(
            new AssertionOptions { Timeout = 500, Message = "Dashboard heading should be visible" });
    }
}
