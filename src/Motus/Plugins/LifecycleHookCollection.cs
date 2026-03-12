using Motus.Abstractions;

namespace Motus;

internal sealed class LifecycleHookCollection
{
    private readonly List<ILifecycleHook> _hooks = [];

    internal void Add(ILifecycleHook hook)
    {
        lock (_hooks)
            _hooks.Add(hook);
    }

    private ILifecycleHook[] Snapshot()
    {
        lock (_hooks)
            return [.. _hooks];
    }

    internal async Task FireBeforeNavigationAsync(IPage page, string url)
    {
        foreach (var hook in Snapshot())
        {
            try { await hook.BeforeNavigationAsync(page, url); }
            catch { /* swallow to match event pump error policy */ }
        }
    }

    internal async Task FireAfterNavigationAsync(IPage page, IResponse? response)
    {
        foreach (var hook in Snapshot())
        {
            try { await hook.AfterNavigationAsync(page, response); }
            catch { }
        }
    }

    internal async Task FireBeforeActionAsync(IPage page, string action)
    {
        foreach (var hook in Snapshot())
        {
            try { await hook.BeforeActionAsync(page, action); }
            catch { }
        }
    }

    internal async Task FireAfterActionAsync(IPage page, string action, ActionResult result)
    {
        foreach (var hook in Snapshot())
        {
            try { await hook.AfterActionAsync(page, action, result); }
            catch { }
        }
    }

    internal async Task FireOnPageCreatedAsync(IPage page)
    {
        foreach (var hook in Snapshot())
        {
            try { await hook.OnPageCreatedAsync(page); }
            catch { }
        }
    }

    internal async Task FireOnPageClosedAsync(IPage page)
    {
        foreach (var hook in Snapshot())
        {
            try { await hook.OnPageClosedAsync(page); }
            catch { }
        }
    }

    internal async Task FireOnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs args)
    {
        foreach (var hook in Snapshot())
        {
            try { await hook.OnConsoleMessageAsync(page, args); }
            catch { }
        }
    }

    internal async Task FireOnPageErrorAsync(IPage page, PageErrorEventArgs args)
    {
        foreach (var hook in Snapshot())
        {
            try { await hook.OnPageErrorAsync(page, args); }
            catch { }
        }
    }
}
