using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in plugin that registers all WCAG 2.1 Level A and AA accessibility rules.
/// Registered as a built-in in PluginHost (not via [MotusPlugin]) because
/// internal types are not visible to user assemblies through the source generator.
/// </summary>
internal sealed class AccessibilityRulesPlugin : IPlugin
{
    public string PluginId => "motus.accessibility-rules";
    public string Name => "Accessibility Rules";
    public string Version => "1.0.0";
    public string? Author => "Motus";
    public string? Description => "Built-in WCAG 2.1 Level A and AA accessibility rules.";

    public Task OnLoadedAsync(IPluginContext context)
    {
        context.RegisterAccessibilityRule(new AltTextAccessibilityRule());
        context.RegisterAccessibilityRule(new UnlabeledFormControlRule());
        context.RegisterAccessibilityRule(new MissingLandmarkRule());
        context.RegisterAccessibilityRule(new HeadingHierarchyRule());
        context.RegisterAccessibilityRule(new EmptyButtonRule());
        context.RegisterAccessibilityRule(new EmptyLinkRule());
        context.RegisterAccessibilityRule(new ColorContrastRule());
        context.RegisterAccessibilityRule(new DuplicateIdRule());
        context.RegisterAccessibilityRule(new MissingDocumentLanguageRule());
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync() => Task.CompletedTask;
}
