using System.Text.RegularExpressions;
using Motus.Abstractions;

namespace Motus.Assertions;

public sealed class LocatorAssertions
{
    private readonly Locator _locator;
    private readonly bool _negate;

    internal LocatorAssertions(Locator locator, bool negate = false)
    {
        _locator = locator;
        _negate = negate;
    }

    public LocatorAssertions Not => new(_locator, !_negate);

    private Task RetryAsync(
        Func<CancellationToken, Task<(bool, string)>> condition,
        string name, string expected, AssertionOptions? options) =>
        AssertionRetryHelper.RetryUntilAsync(
            condition, _negate, name, expected,
            _locator.Selector, _locator.PageUrl,
            AssertionRetryHelper.ResolveTimeout(options?.Timeout),
            options?.Message, CancellationToken.None);

    public Task ToBeVisibleAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var visible = await _locator.IsVisibleAsync().ConfigureAwait(false);
            return (visible, visible.ToString());
        }, "ToBeVisible", "visible", options);

    public Task ToBeHiddenAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var hidden = await _locator.IsHiddenAsync().ConfigureAwait(false);
            return (hidden, hidden.ToString());
        }, "ToBeHidden", "hidden", options);

    public Task ToBeEnabledAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var enabled = await _locator.IsEnabledAsync().ConfigureAwait(false);
            return (enabled, enabled.ToString());
        }, "ToBeEnabled", "enabled", options);

    public Task ToBeDisabledAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var disabled = await _locator.IsDisabledAsync().ConfigureAwait(false);
            return (disabled, disabled.ToString());
        }, "ToBeDisabled", "disabled", options);

    public Task ToBeCheckedAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var check = await _locator.IsCheckedAsync().ConfigureAwait(false);
            return (check, check.ToString());
        }, "ToBeChecked", "checked", options);

    public Task ToBeEditableAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var editable = await _locator.IsEditableAsync().ConfigureAwait(false);
            return (editable, editable.ToString());
        }, "ToBeEditable", "editable", options);

    public Task ToBeEmptyAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var empty = await _locator.IsEmptyAsync(ct).ConfigureAwait(false);
            return (empty, empty.ToString());
        }, "ToBeEmpty", "empty", options);

    public Task ToBeAttachedAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            try
            {
                var count = await _locator.CountAsync().ConfigureAwait(false);
                return (count > 0, $"count={count}");
            }
            catch (MotusSelectorException)
            {
                return (false, "count=0");
            }
        }, "ToBeAttached", "attached", options);

    public Task ToBeDetachedAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            try
            {
                var count = await _locator.CountAsync().ConfigureAwait(false);
                return (count == 0, $"count={count}");
            }
            catch (MotusSelectorException)
            {
                return (true, "count=0");
            }
        }, "ToBeDetached", "detached", options);

    public Task ToHaveTextAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var text = await _locator.TextContentAsync().ConfigureAwait(false) ?? "";
            return (text == expected, text);
        }, "ToHaveText", expected, options);

    public Task ToHaveTextAsync(Regex expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var text = await _locator.TextContentAsync().ConfigureAwait(false) ?? "";
            return (expected.IsMatch(text), text);
        }, "ToHaveText", expected.ToString(), options);

    public Task ToContainTextAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var text = await _locator.TextContentAsync().ConfigureAwait(false) ?? "";
            return (text.Contains(expected, StringComparison.Ordinal), text);
        }, "ToContainText", expected, options);

    public Task ToHaveValueAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var value = await _locator.InputValueAsync().ConfigureAwait(false);
            return (value == expected, value);
        }, "ToHaveValue", expected, options);

    public Task ToHaveAttributeAsync(string name, string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var attr = await _locator.GetAttributeAsync(name).ConfigureAwait(false);
            return (attr == expected, attr ?? "<null>");
        }, "ToHaveAttribute", $"{name}={expected}", options);

    public Task ToHaveClassAsync(string className, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var has = await _locator.HasClassAsync(className, ct).ConfigureAwait(false);
            return (has, has.ToString());
        }, "ToHaveClass", className, options);

    public Task ToHaveCSSAsync(string property, string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var value = await _locator.GetComputedStyleAsync(property, ct).ConfigureAwait(false) ?? "";
            return (value == expected, value);
        }, "ToHaveCSS", $"{property}: {expected}", options);

    public Task ToHaveCountAsync(int expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var count = await _locator.CountAsync().ConfigureAwait(false);
            return (count == expected, count.ToString());
        }, "ToHaveCount", expected.ToString(), options);
}
