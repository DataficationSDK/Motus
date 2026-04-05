using Motus.Abstractions;

namespace Motus.Assertions;

public sealed class PageAssertions
{
    private readonly Page _page;
    private readonly bool _negate;

    internal PageAssertions(Page page, bool negate = false)
    {
        _page = page;
        _negate = negate;
    }

    public PageAssertions Not => new(_page, !_negate);

    private Task RetryAsync(
        Func<CancellationToken, Task<(bool, string)>> condition,
        string name, string expected, AssertionOptions? options) =>
        AssertionRetryHelper.RetryUntilAsync(
            condition, _negate, name, expected,
            selector: null, pageUrl: _page.Url,
            AssertionRetryHelper.ResolveTimeout(options?.Timeout),
            options?.Message, CancellationToken.None);

    public Task ToHaveUrlAsync(string urlOrGlob, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var url = _page.Url;
            var matches = Page.UrlMatchesStatic(url, urlOrGlob);
            return (matches, url);
        }, "ToHaveUrl", urlOrGlob, options);

    public Task ToHaveTitleAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var title = await _page.TitleAsync().ConfigureAwait(false);
            return (title == expected, title);
        }, "ToHaveTitle", expected, options);

    public async Task ToPassAccessibilityAuditAsync(
        Action<AccessibilityAssertionOptions>? configure = null,
        AssertionOptions? options = null)
    {
        var a11yOptions = new AccessibilityAssertionOptions();
        configure?.Invoke(a11yOptions);

        var result = _page.LastAccessibilityAudit;
        if (result is null)
        {
            // On-demand audit: no hook stored a result, so run one now
            var rules = FilterRules(
                _page.ContextInternal.AccessibilityRules.Snapshot(),
                a11yOptions.SkippedRules);
            result = await _page.RunAccessibilityAuditAsync(rules, CancellationToken.None)
                .ConfigureAwait(false);
        }

        var violations = FilterViolations(result.Violations, a11yOptions);
        var hasViolations = violations.Count > 0;
        var passed = _negate ? hasViolations : !hasViolations;

        if (!passed)
        {
            var negateLabel = _negate ? "NOT " : "";
            var expected = $"{negateLabel}0 accessibility violations";
            var actual = FormatViolations(violations);
            var message = options?.Message
                ?? $"Assertion {negateLabel}ToPassAccessibilityAudit failed."
                   + $" Expected: {expected}. Found {violations.Count} violation(s)."
                   + (_page.Url is not null ? $" Page: {_page.Url}." : "");

            throw new MotusAssertionException(
                expected: expected,
                actual: actual,
                selector: null,
                pageUrl: _page.Url,
                assertionTimeout: TimeSpan.Zero,
                message: message);
        }
    }

    private static IReadOnlyList<IAccessibilityRule> FilterRules(
        IReadOnlyList<IAccessibilityRule> rules, IReadOnlyList<string> skippedRules)
    {
        if (skippedRules.Count == 0)
            return rules;

        var skipSet = new HashSet<string>(skippedRules, StringComparer.Ordinal);
        return rules.Where(r => !skipSet.Contains(r.RuleId)).ToList();
    }

    private static IReadOnlyList<AccessibilityViolation> FilterViolations(
        IReadOnlyList<AccessibilityViolation> violations,
        AccessibilityAssertionOptions options)
    {
        var filtered = violations.AsEnumerable();

        if (options.SkippedRules.Count > 0)
        {
            var skipSet = new HashSet<string>(options.SkippedRules, StringComparer.Ordinal);
            filtered = filtered.Where(v => !skipSet.Contains(v.RuleId));
        }

        if (!options.IncludeWarnings)
            filtered = filtered.Where(v => v.Severity == AccessibilityViolationSeverity.Error);
        else
            filtered = filtered.Where(v =>
                v.Severity is AccessibilityViolationSeverity.Error
                           or AccessibilityViolationSeverity.Warning);

        return filtered.ToList();
    }

    private static string FormatViolations(IReadOnlyList<AccessibilityViolation> violations)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Accessibility audit failed with {violations.Count} violation(s):");
        foreach (var v in violations)
        {
            sb.Append($"  [{v.Severity}] {v.RuleId}: {v.Message}");
            if (v.Selector is not null)
                sb.Append($" (selector: {v.Selector})");
            sb.AppendLine();
        }
        return sb.ToString().TrimEnd();
    }
}
