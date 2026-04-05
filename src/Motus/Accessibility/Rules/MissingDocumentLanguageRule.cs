using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Checks that the document has a lang attribute on the html element.
/// Uses pre-fetched DocumentLanguage from the audit context.
/// </summary>
internal sealed class MissingDocumentLanguageRule : IAccessibilityRule
{
    public string RuleId => "a11y-missing-lang";

    public string Description =>
        "The <html> element must have a lang attribute to identify the document language.";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        // Only evaluate once: on the first node in the tree
        if (context.AllNodes.Count == 0 || !ReferenceEquals(node, context.AllNodes[0]))
            return null;

        if (!string.IsNullOrWhiteSpace(context.DocumentLanguage))
            return null;

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Error,
            Message: "Document has no lang attribute. Add lang=\"en\" (or appropriate language) to the <html> element.",
            NodeRole: null,
            NodeName: null,
            BackendDOMNodeId: null,
            Selector: "html");
    }
}
