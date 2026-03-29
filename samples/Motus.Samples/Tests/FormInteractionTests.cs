namespace Motus.Samples.Tests;

/// <summary>
/// Form interactions including fill, type, press, check, select, and clear.
/// </summary>
[TestClass]
public class FormInteractionTests : MotusTestBase
{
    [TestMethod]
    public async Task FillAsync_PopulatesTextField()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.LoginForm);

        // FillAsync clears the field first, then sets the value instantly
        var email = Page.GetByLabel("Email");
        await email.FillAsync("alice@example.com");
        await Expect.That(email).ToHaveValueAsync("alice@example.com");
    }

    [TestMethod]
    public async Task TypeAsync_SimulatesKeystrokes()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.LoginForm);

        // TypeAsync dispatches individual key events with an optional delay between them.
        // A delay of 50ms prevents macOS from dropping keystrokes under compositor pressure.
        var password = Page.GetByLabel("Password");
        await password.TypeAsync("secret123", new KeyboardTypeOptions(Delay: 50));
        await Expect.That(password).ToHaveValueAsync("secret123");
    }

    [TestMethod]
    public async Task PressAsync_SubmitsForm()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.LoginForm);

        await Page.GetByLabel("Email").FillAsync("bob@example.com");

        // PressAsync sends a single key; Enter triggers the form's onsubmit
        await Page.GetByLabel("Email").PressAsync("Enter");

        var feedback = Page.Locator("#feedback");
        await Expect.That(feedback).ToBeVisibleAsync();
        await Expect.That(feedback).ToHaveTextAsync("Welcome, bob@example.com!");
    }

    [TestMethod]
    public async Task CheckAsync_TogglesCheckbox()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.LoginForm);

        var remember = Page.Locator("#remember");
        await remember.CheckAsync();

        // ToBeCheckedAsync auto-retries until the checkbox is checked
        await Expect.That(remember).ToBeCheckedAsync();
    }

    [TestMethod]
    public async Task SelectOptionAsync_ChoosesValue()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.LoginForm);

        // SelectOptionAsync picks option(s) by value
        var role = Page.GetByLabel("Role");
        await role.SelectOptionAsync("admin");
        await Expect.That(role).ToHaveValueAsync("admin");
    }

    [TestMethod]
    public async Task ClearAsync_EmptiesInput()
    {
        await Fixtures.SetPageContentAsync(Page,Fixtures.LoginForm);

        var email = Page.GetByLabel("Email");
        await email.FillAsync("filled@example.com");
        await Expect.That(email).ToHaveValueAsync("filled@example.com");

        // ClearAsync removes all text from the input
        await email.ClearAsync();
        await Expect.That(email).ToBeEmptyAsync();
    }
}
