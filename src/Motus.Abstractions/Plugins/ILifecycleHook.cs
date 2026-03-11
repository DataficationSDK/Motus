namespace Motus.Abstractions;

/// <summary>
/// Provides hooks into the browser automation lifecycle for cross-cutting concerns.
/// </summary>
public interface ILifecycleHook
{
    /// <summary>
    /// Called before a navigation occurs.
    /// </summary>
    /// <param name="page">The page navigating.</param>
    /// <param name="url">The target URL.</param>
    Task BeforeNavigationAsync(IPage page, string url);

    /// <summary>
    /// Called after a navigation completes.
    /// </summary>
    /// <param name="page">The page that navigated.</param>
    /// <param name="response">The navigation response, or null.</param>
    Task AfterNavigationAsync(IPage page, IResponse? response);

    /// <summary>
    /// Called before an action is performed on a locator.
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="action">The action name (e.g. "click", "fill").</param>
    /// <param name="selector">The selector being acted on.</param>
    Task BeforeActionAsync(IPage page, string action, string selector);

    /// <summary>
    /// Called after an action is performed on a locator.
    /// </summary>
    /// <param name="page">The page containing the element.</param>
    /// <param name="action">The action name.</param>
    /// <param name="selector">The selector that was acted on.</param>
    Task AfterActionAsync(IPage page, string action, string selector);

    /// <summary>
    /// Called when a new page is created.
    /// </summary>
    /// <param name="page">The new page.</param>
    Task OnPageCreatedAsync(IPage page);

    /// <summary>
    /// Called when a page is closed.
    /// </summary>
    /// <param name="page">The page that was closed.</param>
    Task OnPageClosedAsync(IPage page);
}
