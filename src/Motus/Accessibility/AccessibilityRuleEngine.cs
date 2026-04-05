using System.Diagnostics;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Walks the accessibility tree and invokes all registered rules on each node.
/// </summary>
internal sealed class AccessibilityRuleEngine
{
    private readonly IReadOnlyList<IAccessibilityRule> _rules;

    internal AccessibilityRuleEngine(IReadOnlyList<IAccessibilityRule> rules)
    {
        _rules = rules;
    }

    /// <summary>
    /// Executes the rule engine against the provided node list and context.
    /// </summary>
    internal AccessibilityAuditResult Run(
        IReadOnlyList<AccessibilityNode> nodes,
        AccessibilityAuditContext context,
        string? diagnosticMessage = null)
    {
        var sw = Stopwatch.StartNew();

        if (nodes.Count == 0 || _rules.Count == 0)
        {
            return new AccessibilityAuditResult(
                Violations: [],
                PassCount: 0,
                ViolationCount: 0,
                Duration: sw.Elapsed,
                DiagnosticMessage: diagnosticMessage);
        }

        // Deduplication keyed on (RuleId, dedupeKey) where dedupeKey prefers
        // BackendDOMNodeId for stability, falling back to NodeId for virtual nodes.
        var seen = new HashSet<(string ruleId, string dedupeKey)>();
        var violations = new List<AccessibilityViolation>();
        int passCount = 0;

        foreach (var node in nodes)
        {
            foreach (var rule in _rules)
            {
                var violation = rule.Evaluate(node, context);
                if (violation is null)
                {
                    passCount++;
                    continue;
                }

                var dedupeKey = node.BackendDOMNodeId.HasValue
                    ? node.BackendDOMNodeId.Value.ToString()
                    : node.NodeId;

                if (seen.Add((violation.RuleId, dedupeKey)))
                    violations.Add(violation);
            }
        }

        return new AccessibilityAuditResult(
            Violations: violations,
            PassCount: passCount,
            ViolationCount: violations.Count,
            Duration: sw.Elapsed,
            DiagnosticMessage: diagnosticMessage);
    }
}
