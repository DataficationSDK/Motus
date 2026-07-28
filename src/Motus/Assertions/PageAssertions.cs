using Motus.Abstractions;

namespace Motus.Assertions;

/// <summary>
/// Assertions about a page as a whole, obtained from <see cref="Expect.That(IPage)"/>.
/// </summary>
/// <remarks>
/// The URL, title and performance assertions re-evaluate until they hold or the timeout elapses,
/// so a navigation still in flight is waited for rather than failed on. The accessibility audit is
/// the exception and is evaluated once, since it describes the document as it stands.
/// </remarks>
public sealed class PageAssertions
{
    private readonly Page _page;
    private readonly bool _negate;

    internal PageAssertions(Page page, bool negate = false)
    {
        _page = page;
        _negate = negate;
    }

    /// <summary>
    /// Inverts the assertion that follows, so it passes when the condition does not hold.
    /// </summary>
    public PageAssertions Not => new(_page, !_negate);

    private Task RetryAsync(
        Func<CancellationToken, Task<(bool, string)>> condition,
        string name, string expected, AssertionOptions? options) =>
        AssertionRetryHelper.RetryUntilAsync(
            condition, _negate, name, expected,
            selector: null, pageUrl: _page.Url,
            AssertionRetryHelper.ResolveTimeout(options?.Timeout),
            options?.Message, CancellationToken.None);

    /// <summary>Asserts that the page's current URL equals or matches the expected pattern.</summary>
    /// <param name="urlOrGlob">
    /// A full URL, or a pattern containing <c>*</c> for any run of characters.
    /// </param>
    /// <param name="options">Timeout and failure message overrides.</param>
    /// <remarks>
    /// Three forms are accepted, and which one applies depends on the pattern. A pattern equal to
    /// the URL matches it. A pattern containing <c>*</c> is matched against the whole URL, so
    /// <c>https://example.com/orders/*</c> matches any order page but not the site root. A pattern
    /// containing no <c>*</c> at all matches when the URL merely contains it, which is worth
    /// knowing: <c>/orders</c> also matches <c>https://example.com/orders/17/edit</c>.
    /// </remarks>
    public Task ToHaveUrlAsync(string urlOrGlob, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var url = _page.Url;
            var matches = Page.UrlMatchesStatic(url, urlOrGlob);
            return (matches, url);
        }, "ToHaveUrl", urlOrGlob, options);

    /// <summary>Asserts that the document title equals the expected string.</summary>
    /// <param name="expected">The title the document must carry.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveTitleAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var title = await _page.TitleAsync().ConfigureAwait(false);
            return (title == expected, title);
        }, "ToHaveTitle", expected, options);

    /// <summary>
    /// Asserts that the page raises no accessibility violations, and reports every violation it
    /// found when it does.
    /// </summary>
    /// <param name="configure">
    /// Narrows what counts as a failure: rules to skip, and whether warnings fail alongside errors.
    /// </param>
    /// <param name="options">Failure message override. The timeout is unused, since no retry occurs.</param>
    /// <remarks>
    /// A result already collected by the audit hook is reused; without one, an audit runs here
    /// against the rules registered on the context. Unlike the other page assertions this is
    /// evaluated once rather than retried, so wait for the page to settle before calling it.
    /// </remarks>
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

    /// <summary>
    /// Asserts that every metric in the active performance budget is within its threshold, and
    /// names the ones that are not when it fails.
    /// </summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    /// <remarks>
    /// The budget is resolved in order from the one applied to the page, then the one carried by
    /// the ambient test context, then the thresholds in configuration. When none of those supplies
    /// a budget the call throws <see cref="InvalidOperationException"/> rather than passing, since
    /// an assertion with nothing to assert would otherwise report success.
    /// </remarks>
    /// <exception cref="InvalidOperationException">No performance budget is in effect.</exception>
    public async Task ToMeetPerformanceBudgetAsync(AssertionOptions? options = null)
    {
        var budget = ResolveBudget();

        await RetryAsync(async ct =>
        {
            await _page.RefreshPerformanceMetricsAsync(ct).ConfigureAwait(false);
            var metrics = _page.LastPerformanceMetrics;
            if (metrics is null)
                return (false, "<no metrics collected>");

            var result = budget.Evaluate(metrics);
            var actual = result.Passed
                ? "all metrics within budget"
                : FormatBudgetFailures(result);
            return (result.Passed, actual);
        }, "ToMeetPerformanceBudget", "all metrics within budget", options)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Asserts that Largest Contentful Paint, the time until the largest element in the viewport
    /// has rendered, is at or below the given threshold.
    /// </summary>
    /// <param name="thresholdMs">The upper bound in milliseconds.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveLcpBelowAsync(double thresholdMs, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            await _page.RefreshPerformanceMetricsAsync(ct).ConfigureAwait(false);
            var metrics = _page.LastPerformanceMetrics;
            if (metrics?.Lcp is null)
                return (false, "<LCP not collected>");
            var actual = metrics.Lcp.Value;
            return (actual <= thresholdMs, $"{actual:F1}ms");
        }, "ToHaveLcpBelow", $"< {thresholdMs}ms", options);

    /// <summary>
    /// Asserts that First Contentful Paint, the time until any content first appears, is at or
    /// below the given threshold.
    /// </summary>
    /// <param name="thresholdMs">The upper bound in milliseconds.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveFcpBelowAsync(double thresholdMs, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            await _page.RefreshPerformanceMetricsAsync(ct).ConfigureAwait(false);
            var metrics = _page.LastPerformanceMetrics;
            if (metrics?.Fcp is null)
                return (false, "<FCP not collected>");
            var actual = metrics.Fcp.Value;
            return (actual <= thresholdMs, $"{actual:F1}ms");
        }, "ToHaveFcpBelow", $"< {thresholdMs}ms", options);

    /// <summary>
    /// Asserts that Time To First Byte, the wait before the server's response begins arriving, is
    /// at or below the given threshold.
    /// </summary>
    /// <param name="thresholdMs">The upper bound in milliseconds.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveTtfbBelowAsync(double thresholdMs, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            await _page.RefreshPerformanceMetricsAsync(ct).ConfigureAwait(false);
            var metrics = _page.LastPerformanceMetrics;
            if (metrics?.Ttfb is null)
                return (false, "<TTFB not collected>");
            var actual = metrics.Ttfb.Value;
            return (actual <= thresholdMs, $"{actual:F1}ms");
        }, "ToHaveTtfbBelow", $"< {thresholdMs}ms", options);

    /// <summary>
    /// Asserts that Cumulative Layout Shift, how much the page moved under the reader while
    /// loading, is at or below the given threshold.
    /// </summary>
    /// <param name="threshold">The upper bound, as an unitless score rather than a duration.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveClsBelowAsync(double threshold, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            await _page.RefreshPerformanceMetricsAsync(ct).ConfigureAwait(false);
            var metrics = _page.LastPerformanceMetrics;
            if (metrics?.Cls is null)
                return (false, "<CLS not collected>");
            var actual = metrics.Cls.Value;
            return (actual <= threshold, $"{actual:F3}");
        }, "ToHaveClsBelow", $"< {threshold}", options);

    /// <summary>
    /// Asserts that Interaction to Next Paint, how long the page took to respond visibly to the
    /// reader, is at or below the given threshold.
    /// </summary>
    /// <param name="thresholdMs">The upper bound in milliseconds.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    /// <remarks>
    /// The metric only exists once something has been interacted with, so a page that has merely
    /// been loaded reports none and the assertion fails rather than passing vacuously.
    /// </remarks>
    public Task ToHaveInpBelowAsync(double thresholdMs, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            await _page.RefreshPerformanceMetricsAsync(ct).ConfigureAwait(false);
            var metrics = _page.LastPerformanceMetrics;
            if (metrics?.Inp is null)
                return (false, "<INP not collected>");
            var actual = metrics.Inp.Value;
            return (actual <= thresholdMs, $"{actual:F1}ms");
        }, "ToHaveInpBelow", $"< {thresholdMs}ms", options);

    private PerformanceBudget ResolveBudget()
    {
        if (_page.ActivePerformanceBudget is { } pageBudget)
            return pageBudget;

        if (PerformanceBudgetContext.Current is { } ambient)
            return ambient;

        var configBudget = ConfigMerge.ToBudget(MotusConfigLoader.Config.Performance);
        if (configBudget is not null)
            return configBudget;

        throw new InvalidOperationException(
            "No performance budget is active. Apply [PerformanceBudget] to the test method or class, " +
            "or configure budget thresholds in motus.config.json under the \"performance\" key.");
    }

    private static string FormatBudgetFailures(PerformanceBudgetResult result)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Performance budget exceeded:");
        foreach (var entry in result.Entries)
        {
            if (!entry.Passed)
            {
                var actualStr = entry.ActualValue.HasValue ? $"{entry.ActualValue.Value:F1}" : "null";
                var deltaStr = entry.Delta.HasValue ? $"{entry.Delta.Value:F1}" : "?";
                sb.AppendLine($"  {entry.MetricName}: {actualStr} (budget: {entry.Threshold:F1}, over by {deltaStr})");
            }
        }
        return sb.ToString().TrimEnd();
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
