using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Checks the active page against the built-in WCAG accessibility rules and reports
/// the violations found, each tied to a ref where the offending element can be
/// addressed by other tools.
/// </summary>
[McpServerToolType]
public sealed class AccessibilityTools
{
    [McpServerTool(Name = "audit_accessibility", Title = "Audit accessibility", Destructive = false, ReadOnly = true)]
    [Description("Runs WCAG 2.1 A and AA accessibility checks against the active page and returns the "
        + "violations found, each with a rule id, severity, message, and (when the element is addressable) "
        + "a ref usable with click, type, and other tools. A fresh snapshot is taken as part of the audit, "
        + "so refs from an earlier snapshot are replaced.")]
    public static async Task<CallToolResult> AuditAccessibilityAsync(
        [Description("Only return violations at or above this severity: error, warning, or info. "
            + "Omit to return all severities.")] string? min_severity,
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        AccessibilityViolationSeverity? threshold = null;
        if (!string.IsNullOrWhiteSpace(min_severity))
        {
            if (!Enum.TryParse(min_severity, ignoreCase: true, out AccessibilityViolationSeverity parsed))
                return ToolResultHelper.Error(
                    $"Unknown severity '{min_severity}'. Use error, warning, or info.");
            threshold = parsed;
        }

        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);

            // A fresh snapshot refreshes the ref map so element-level violations can be
            // tied back to a ref the other tools accept.
            var snapshots = pageService.GetSnapshotService(page);
            await snapshots.TakeSnapshotAsync(cancellationToken).ConfigureAwait(false);

            var result = await page.RunAccessibilityAuditAsync(cancellationToken).ConfigureAwait(false);

            // Severity is ordered Error, Warning, Info, so "at or above" a threshold is
            // a lower-or-equal ordinal.
            var violations = threshold is { } min
                ? result.Violations.Where(v => (int)v.Severity <= (int)min).ToList()
                : result.Violations.ToList();

            if (violations.Count == 0)
            {
                var none = threshold is { } onlyAbove
                    ? $"No accessibility violations found at or above {onlyAbove.ToString().ToLowerInvariant()} severity."
                    : "No accessibility violations found.";
                return ToolResultHelper.Text(Append(none, result.DiagnosticMessage));
            }

            var array = new JsonArray();
            foreach (var violation in violations)
            {
                var refId = violation.BackendDOMNodeId is long nodeId
                    ? snapshots.GetRefForNodeId(nodeId)
                    : null;

                array.Add(new JsonObject
                {
                    ["ruleId"] = violation.RuleId,
                    ["severity"] = violation.Severity.ToString(),
                    ["message"] = violation.Message,
                    ["nodeRole"] = violation.NodeRole,
                    ["nodeName"] = violation.NodeName,
                    ["ref"] = refId,
                });
            }

            var payload = new JsonObject
            {
                ["violationCount"] = violations.Count,
                ["violations"] = array,
            };

            // Build the structured value through the node API and parse it, rather than
            // reflection-serializing a type, so the trim and AOT analyzers stay satisfied.
            var element = JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone();
            var summary = Append(
                $"Found {violations.Count} accessibility violation(s).",
                result.DiagnosticMessage);
            return ToolResultHelper.Structured(element, summary);
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Accessibility audit failed: {ex.Message}");
        }
    }

    private static string Append(string text, string? diagnostic)
        => string.IsNullOrEmpty(diagnostic) ? text : $"{text} {diagnostic}";
}
