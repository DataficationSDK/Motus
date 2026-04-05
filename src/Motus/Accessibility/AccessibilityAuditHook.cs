using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in plugin that runs accessibility audits after navigation and (optionally)
/// after mutating actions. Disabled by default; enabled via <see cref="AccessibilityOptions"/>.
/// </summary>
internal sealed class AccessibilityAuditHook : IPlugin, ILifecycleHook
{
    private static readonly HashSet<string> AuditedActions = new(StringComparer.Ordinal)
    {
        "click", "fill", "selectOption"
    };

    private readonly AccessibilityOptions _options;
    private BrowserContext? _context;

    internal AccessibilityAuditHook(AccessibilityOptions? options)
    {
        _options = options ?? new AccessibilityOptions();
    }

    public string PluginId => "motus.accessibility-audit";
    public string Name => "Accessibility Audit Hook";
    public string Version => "1.0.0";
    public string? Author => "Motus";
    public string? Description => "Runs WCAG accessibility audits after navigation and actions.";

    public Task OnLoadedAsync(IPluginContext context)
    {
        if (!_options.Enable)
            return Task.CompletedTask;

        _context = ((PluginContext)context).Context;
        context.RegisterLifecycleHook(this);
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync() => Task.CompletedTask;

    public Task BeforeNavigationAsync(IPage page, string url) => Task.CompletedTask;

    public async Task AfterNavigationAsync(IPage page, IResponse? response)
    {
        if (!_options.AuditAfterNavigation || _context is null)
            return;

        await RunAuditAsync(page).ConfigureAwait(false);
    }

    public Task BeforeActionAsync(IPage page, string action) => Task.CompletedTask;

    public async Task AfterActionAsync(IPage page, string action, ActionResult result)
    {
        if (!_options.AuditAfterActions || _context is null || !AuditedActions.Contains(action))
            return;

        await RunAuditAsync(page).ConfigureAwait(false);
    }

    public Task OnPageCreatedAsync(IPage page) => Task.CompletedTask;
    public Task OnPageClosedAsync(IPage page) => Task.CompletedTask;
    public Task OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message) => Task.CompletedTask;
    public Task OnPageErrorAsync(IPage page, PageErrorEventArgs error) => Task.CompletedTask;

    private async Task RunAuditAsync(IPage page)
    {
        var concrete = (Page)page;
        var rules = FilterRules(_context!.AccessibilityRules.Snapshot());
        var result = await concrete.RunAccessibilityAuditAsync(rules, CancellationToken.None)
            .ConfigureAwait(false);
        concrete.LastAccessibilityAudit = result;
    }

    private IReadOnlyList<IAccessibilityRule> FilterRules(IReadOnlyList<IAccessibilityRule> rules)
    {
        var skip = _options.SkipRules;
        if (skip is null or { Count: 0 })
            return rules;

        var skipSet = new HashSet<string>(skip, StringComparer.Ordinal);
        return rules.Where(r => !skipSet.Contains(r.RuleId)).ToList();
    }
}
