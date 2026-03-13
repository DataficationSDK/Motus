namespace Motus.Samples.PageObjects;

/// <summary>
/// Page Object Model for the Todo App fixture.
/// This mirrors the pattern that <c>dotnet motus codegen</c> auto-generates:
/// locator properties for key elements, and action methods that compose them.
/// </summary>
public class TodoAppPage
{
    private readonly IPage _page;

    public TodoAppPage(IPage page) => _page = page;

    // -- Locator properties --

    public ILocator NewTodoInput => _page.GetByPlaceholder("What needs to be done?");
    public ILocator AddButton => _page.GetByRole("button", "Add");
    public ILocator TodoItems => _page.Locator(".todo-item");
    public ILocator ClearCompletedButton => _page.Locator("#clear-completed");
    public ILocator ActiveCountBadge => _page.GetByTestId("active-count");

    // -- Action methods --

    /// <summary>Types a todo and clicks Add.</summary>
    public async Task AddTodoAsync(string text)
    {
        await NewTodoInput.FillAsync(text);
        await AddButton.ClickAsync();
    }

    /// <summary>Checks the checkbox on the todo at the given zero-based index.</summary>
    public async Task CompleteTodoAsync(int index)
    {
        await TodoItems.Nth(index).Locator("input[type='checkbox']").CheckAsync();
    }

    /// <summary>Returns the text of every todo item in order.</summary>
    public async Task<IReadOnlyList<string>> GetAllTodoTextsAsync()
    {
        return await TodoItems.Locator("span").AllInnerTextsAsync();
    }
}
