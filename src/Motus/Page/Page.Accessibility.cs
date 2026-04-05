using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    /// <summary>
    /// The most recent accessibility audit result, set by <see cref="AccessibilityAuditHook"/>
    /// after navigation or action. Null when the hook is disabled or no audit has run.
    /// </summary>
    internal AccessibilityAuditResult? LastAccessibilityAudit { get; set; }

    /// <summary>
    /// Runs the registered accessibility rules against this page's accessibility tree.
    /// Pre-fetches computed styles, duplicate IDs, and document language for rules that need them.
    /// </summary>
    internal async Task<AccessibilityAuditResult> RunAccessibilityAuditAsync(
        IReadOnlyList<IAccessibilityRule> rules,
        CancellationToken ct)
    {
        var query = new AccessibilityTreeQuery(_session);
        var treeResult = await query.GetTreeAsync(ct).ConfigureAwait(false);

        var nodes = treeResult.AllWalkableNodes;

        // Run pre-fetch collectors in parallel
        var stylesTask = ComputedStyleCollector.CollectAsync(_session, nodes, ct);
        var duplicateIdsTask = DuplicateIdCollector.CollectAsync(_session, ct);
        var langTask = DocumentLanguageCollector.CollectAsync(_session, ct);

        await Task.WhenAll(stylesTask, duplicateIdsTask, langTask).ConfigureAwait(false);

        var context = new AccessibilityAuditContext(
            AllNodes: nodes,
            Page: this,
            ComputedStyles: await stylesTask.ConfigureAwait(false),
            DuplicateIds: await duplicateIdsTask.ConfigureAwait(false),
            DocumentLanguage: await langTask.ConfigureAwait(false));

        var engine = new AccessibilityRuleEngine(rules);
        return engine.Run(nodes, context, treeResult.DiagnosticMessage);
    }
}
