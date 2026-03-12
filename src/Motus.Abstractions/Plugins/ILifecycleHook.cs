namespace Motus.Abstractions;

/// <summary>
/// Hooks into the browser automation lifecycle for cross-cutting concerns
/// (logging, performance measurement, automatic retries).
/// </summary>
public interface ILifecycleHook
{
    /// <summary>
    /// Called before each page navigation.
    /// </summary>
    /// <param name="page">The page being navigated.</param>
    /// <param name="url">The target URL.</param>
    Task BeforeNavigationAsync(IPage page, string url);

    /// <summary>
    /// Called after each navigation completes.
    /// </summary>
    /// <param name="page">The page that was navigated.</param>
    /// <param name="response">The navigation response, or null if navigation produced no response.</param>
    Task AfterNavigationAsync(IPage page, IResponse? response);

    /// <summary>
    /// Called before each user action (click, fill, type).
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="action">The action name (e.g. "click", "fill").</param>
    Task BeforeActionAsync(IPage page, string action);

    /// <summary>
    /// Called after each user action completes.
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="action">The action name.</param>
    /// <param name="result">The outcome of the action. Error is null on success.</param>
    Task AfterActionAsync(IPage page, string action, ActionResult result);

    /// <summary>
    /// Called when a new page is created.
    /// </summary>
    /// <param name="page">The newly created page.</param>
    Task OnPageCreatedAsync(IPage page);

    /// <summary>
    /// Called when a page is closed.
    /// </summary>
    /// <param name="page">The page being closed.</param>
    Task OnPageClosedAsync(IPage page);

    /// <summary>
    /// Called when a console message is emitted.
    /// </summary>
    /// <param name="page">The page that emitted the message.</param>
    /// <param name="message">The console message details.</param>
    Task OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message);

    /// <summary>
    /// Called when an uncaught page error occurs.
    /// </summary>
    /// <param name="page">The page where the error occurred.</param>
    /// <param name="error">The error details including message and stack trace.</param>
    Task OnPageErrorAsync(IPage page, PageErrorEventArgs error);
}
