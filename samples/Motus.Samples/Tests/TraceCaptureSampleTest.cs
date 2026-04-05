namespace Motus.Samples.Tests;

/// <summary>
/// Captures a rich trace ZIP with screenshots and multiple interaction steps.
/// Run this test to generate a sample trace file for use with <c>motus trace show</c>.
/// </summary>
[TestClass]
public class TraceCaptureSampleTest : MotusTestBase
{
    [TestMethod]
    public async Task CaptureFullWorkflowTrace()
    {
        // Write next to the project file so `motus trace show` can find it easily.
        // Resolve the Motus.Samples project root from the assembly location.
        var projectDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));
        var tracePath = Path.Combine(projectDir, "sample-trace.zip");

        // Stop any pre-existing tracing (e.g. from failure-tracing) before starting fresh
        await Context.Tracing.StopAsync();

        await Context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
        });

        // Step 1: Navigate to the Todo App
        await Fixtures.SetPageContentAsync(Page, Fixtures.TodoApp);

        // Step 2: Add several todo items
        var input = Page.GetByPlaceholder("What needs to be done?");
        var addBtn = Page.GetByRole("button", "Add");

        await input.FillAsync("Buy groceries");
        await addBtn.ClickAsync();

        await input.FillAsync("Walk the dog");
        await addBtn.ClickAsync();

        await input.FillAsync("Write tests");
        await addBtn.ClickAsync();

        await input.FillAsync("Review PR");
        await addBtn.ClickAsync();

        // Step 3: Verify items were added
        await Expect.That(Page.Locator(".todo-item")).ToHaveCountAsync(4);

        // Step 4: Complete a couple of items
        var checkboxes = Page.Locator(".todo-item input[type='checkbox']");
        await checkboxes.Nth(0).ClickAsync();
        await checkboxes.Nth(2).ClickAsync();

        // Step 5: Verify active count
        await Expect.That(Page.GetByTestId("active-count")).ToHaveTextAsync("2 items left");

        // Step 6: Clear completed
        await Page.Locator("#clear-completed").ClickAsync();
        await Expect.That(Page.Locator(".todo-item")).ToHaveCountAsync(2);

        // Step 7: Navigate to the Dashboard
        await Fixtures.SetPageContentAsync(Page, Fixtures.Dashboard);
        await Expect.That(Page).ToHaveTitleAsync("Dashboard");

        // Step 8: Interact with the dashboard
        await Page.Locator("#toggle-sidebar").ClickAsync();
        await Expect.That(Page.Locator("#sidebar")).ToBeHiddenAsync();

        await Page.Locator("#toggle-sidebar").ClickAsync();
        await Expect.That(Page.Locator("#sidebar")).ToBeVisibleAsync();

        // Step 9: Navigate to the Login Form
        await Fixtures.SetPageContentAsync(Page, Fixtures.LoginForm);

        // Step 10: Fill in the login form
        await Page.GetByLabel("Email").FillAsync("tester@example.com");
        await Page.GetByLabel("Password").FillAsync("secret123");
        var remember = Page.Locator("#remember");
        await remember.CheckAsync();
        await Page.GetByLabel("Role").SelectOptionAsync("admin");

        // Step 11: Submit the form
        await Page.GetByLabel("Email").PressAsync("Enter");
        await Expect.That(Page.Locator("#feedback")).ToBeVisibleAsync();
        await Expect.That(Page.Locator("#feedback")).ToHaveTextAsync("Welcome, tester@example.com!");

        // Stop tracing and save
        await Context.Tracing.StopAsync(new TracingStopOptions { Path = tracePath });

        Assert.IsTrue(File.Exists(tracePath), "Trace ZIP should be created");
        var info = new FileInfo(tracePath);
        Assert.IsTrue(info.Length > 0, "Trace ZIP should be non-empty");
    }
}
