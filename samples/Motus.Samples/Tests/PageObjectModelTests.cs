using Motus.Samples.PageObjects;

namespace Motus.Samples.Tests;

/// <summary>
/// Phase 3E: Page Object Model pattern using TodoAppPage.
/// Demonstrates the same POM structure that <c>dotnet motus codegen</c> produces.
/// </summary>
[TestClass]
public class PageObjectModelTests : MotusTestBase
{
    private TodoAppPage _todo = null!;

    [TestInitialize]
    public async Task SetUp()
    {
        await Page.SetContentAsync(Fixtures.TodoApp);
        _todo = new TodoAppPage(Page);
    }

    [TestMethod]
    public async Task AddTodo_AppearsInList()
    {
        await _todo.AddTodoAsync("Buy milk");

        var texts = await _todo.GetAllTodoTextsAsync();
        CollectionAssert.Contains(texts.ToList(), "Buy milk");
    }

    [TestMethod]
    public async Task CompleteTodo_UpdatesActiveCount()
    {
        await _todo.AddTodoAsync("Walk the dog");
        await Expect.That(_todo.ActiveCountBadge).ToHaveTextAsync("1 items left");

        await _todo.CompleteTodoAsync(0);
        await Expect.That(_todo.ActiveCountBadge).ToHaveTextAsync("0 items left");
    }

    [TestMethod]
    public async Task ClearCompleted_RemovesFinishedItems()
    {
        await _todo.AddTodoAsync("Task A");
        await _todo.AddTodoAsync("Task B");
        await _todo.CompleteTodoAsync(0);

        await _todo.ClearCompletedButton.ClickAsync();

        // Only the non-completed item should remain
        await Expect.That(_todo.TodoItems).ToHaveCountAsync(1);
        await Expect.That(_todo.TodoItems.First).ToContainTextAsync("Task B");
    }

    [TestMethod]
    public async Task MultipleTodos_MaintainOrder()
    {
        await _todo.AddTodoAsync("First");
        await _todo.AddTodoAsync("Second");
        await _todo.AddTodoAsync("Third");

        var texts = await _todo.GetAllTodoTextsAsync();
        Assert.AreEqual(3, texts.Count);
        Assert.AreEqual("First", texts[0]);
        Assert.AreEqual("Second", texts[1]);
        Assert.AreEqual("Third", texts[2]);
    }
}
