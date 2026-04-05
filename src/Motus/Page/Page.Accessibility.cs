using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    /// <summary>
    /// Runs the registered accessibility rules against this page's accessibility tree.
    /// </summary>
    internal async Task<AccessibilityAuditResult> RunAccessibilityAuditAsync(
        IReadOnlyList<IAccessibilityRule> rules,
        CancellationToken ct)
    {
        var query = new AccessibilityTreeQuery(_session);
        var treeResult = await query.GetTreeAsync(ct).ConfigureAwait(false);

        var context = new AccessibilityAuditContext(
            AllNodes: treeResult.AllWalkableNodes,
            Page: this);

        var engine = new AccessibilityRuleEngine(rules);
        return engine.Run(treeResult.AllWalkableNodes, context, treeResult.DiagnosticMessage);
    }
}
