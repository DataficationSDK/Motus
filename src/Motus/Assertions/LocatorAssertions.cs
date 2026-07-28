using System.Text.RegularExpressions;
using Motus.Abstractions;

namespace Motus.Assertions;

/// <summary>
/// Assertions about a single locator, obtained from <see cref="Expect.That(ILocator)"/>.
/// </summary>
/// <remarks>
/// Every assertion here re-evaluates its condition until it holds or the timeout elapses, so a
/// value the page has not settled on yet is waited for rather than failed on, which makes an
/// assertion after a navigation or an interaction safe. The element itself is a separate question:
/// these need it present already and fail at once when the locator matches nothing, rather than
/// waiting for it to appear, so wait for one that has still to render with
/// <c>ToBeAttachedAsync</c> first.
/// The timeout and the message on failure come from the <see cref="AssertionOptions"/> each method
/// accepts.
/// </remarks>
public sealed class LocatorAssertions
{
    private readonly Locator _locator;
    private readonly bool _negate;

    internal LocatorAssertions(Locator locator, bool negate = false)
    {
        _locator = locator;
        _negate = negate;
    }

    /// <summary>
    /// Inverts the assertion that follows, so it passes when the condition does not hold.
    /// </summary>
    /// <remarks>
    /// The retry still applies, inverted with it: a negated assertion waits for the condition to
    /// stop holding rather than passing the moment it does not hold yet.
    /// </remarks>
    public LocatorAssertions Not => new(_locator, !_negate);

    private Task RetryAsync(
        Func<CancellationToken, Task<(bool, string)>> condition,
        string name, string expected, AssertionOptions? options) =>
        AssertionRetryHelper.RetryUntilAsync(
            condition, _negate, name, expected,
            _locator.Selector, _locator.ContextUrl,
            AssertionRetryHelper.ResolveTimeout(options?.Timeout),
            options?.Message, CancellationToken.None);

    /// <summary>Asserts that the element is present in the DOM and visible on the page.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToBeVisibleAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var visible = await _locator.IsVisibleAsync().ConfigureAwait(false);
            return (visible, visible.ToString());
        }, "ToBeVisible", "visible", options);

    /// <summary>Asserts that the element is absent from the DOM or not visible on the page.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToBeHiddenAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var hidden = await _locator.IsHiddenAsync().ConfigureAwait(false);
            return (hidden, hidden.ToString());
        }, "ToBeHidden", "hidden", options);

    /// <summary>Asserts that the element accepts interaction rather than being disabled.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToBeEnabledAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var enabled = await _locator.IsEnabledAsync().ConfigureAwait(false);
            return (enabled, enabled.ToString());
        }, "ToBeEnabled", "enabled", options);

    /// <summary>Asserts that the element is disabled and will not accept interaction.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToBeDisabledAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var disabled = await _locator.IsDisabledAsync().ConfigureAwait(false);
            return (disabled, disabled.ToString());
        }, "ToBeDisabled", "disabled", options);

    /// <summary>Asserts that the checkbox or radio button is checked.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToBeCheckedAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var check = await _locator.IsCheckedAsync().ConfigureAwait(false);
            return (check, check.ToString());
        }, "ToBeChecked", "checked", options);

    /// <summary>Asserts that the element accepts typed input rather than being read-only.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToBeEditableAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var editable = await _locator.IsEditableAsync().ConfigureAwait(false);
            return (editable, editable.ToString());
        }, "ToBeEditable", "editable", options);

    /// <summary>Asserts that the element has no text and no child elements.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToBeEmptyAsync(AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var empty = await _locator.IsEmptyAsync(ct).ConfigureAwait(false);
            return (empty, empty.ToString());
        }, "ToBeEmpty", "empty", options);

    /// <summary>Asserts that at least one element matches the locator in the DOM.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
    /// <remarks>
    /// Attachment says nothing about visibility. An element behind a closed accordion is attached;
    /// use <see cref="ToBeVisibleAsync"/> when the question is whether it can be seen.
    /// </remarks>
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

    /// <summary>Asserts that no element matches the locator in the DOM.</summary>
    /// <param name="options">Timeout and failure message overrides.</param>
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

    /// <summary>Asserts that the element's text content equals the expected string exactly.</summary>
    /// <param name="expected">The full text the element must carry.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveTextAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var text = await _locator.TextContentAsync().ConfigureAwait(false) ?? "";
            return (text == expected, text);
        }, "ToHaveText", expected, options);

    /// <summary>Asserts that the element's text content matches the expected pattern.</summary>
    /// <param name="expected">The pattern the text must match.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveTextAsync(Regex expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var text = await _locator.TextContentAsync().ConfigureAwait(false) ?? "";
            return (expected.IsMatch(text), text);
        }, "ToHaveText", expected.ToString(), options);

    /// <summary>Asserts that the element's text content contains the expected substring.</summary>
    /// <param name="expected">The substring the text must contain.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToContainTextAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var text = await _locator.TextContentAsync().ConfigureAwait(false) ?? "";
            return (text.Contains(expected, StringComparison.Ordinal), text);
        }, "ToContainText", expected, options);

    /// <summary>Asserts that the input element's current value equals the expected string.</summary>
    /// <param name="expected">The value the input must hold.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveValueAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var value = await _locator.InputValueAsync().ConfigureAwait(false);
            return (value == expected, value);
        }, "ToHaveValue", expected, options);

    /// <summary>Asserts that the element carries an attribute with the expected value.</summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="expected">The value the attribute must hold.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveAttributeAsync(string name, string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var attr = await _locator.GetAttributeAsync(name).ConfigureAwait(false);
            return (attr == expected, attr ?? "<null>");
        }, "ToHaveAttribute", $"{name}={expected}", options);

    /// <summary>Asserts that the element's class list includes the named class.</summary>
    /// <param name="className">The single class name to look for.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveClassAsync(string className, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var has = await _locator.HasClassAsync(className, ct).ConfigureAwait(false);
            return (has, has.ToString());
        }, "ToHaveClass", className, options);

    /// <summary>Asserts that a computed CSS property resolves to the expected value.</summary>
    /// <param name="property">The CSS property name, as the browser computes it.</param>
    /// <param name="expected">The computed value to match, in the browser's own serialization.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    /// <remarks>
    /// The comparison is against the computed value rather than the authored one, so a color
    /// written as a keyword or a hex triple is reported in the browser's normalized form.
    /// </remarks>
    public Task ToHaveCSSAsync(string property, string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var value = await _locator.GetComputedStyleAsync(property, ct).ConfigureAwait(false) ?? "";
            return (value == expected, value);
        }, "ToHaveCSS", $"{property}: {expected}", options);

    /// <summary>Asserts that the locator matches exactly the expected number of elements.</summary>
    /// <param name="expected">The number of matches required.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    public Task ToHaveCountAsync(int expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var count = await _locator.CountAsync().ConfigureAwait(false);
            return (count == expected, count.ToString());
        }, "ToHaveCount", expected.ToString(), options);

    /// <summary>
    /// Asserts that the element's accessible name, as the browser computes it, equals the
    /// expected string.
    /// </summary>
    /// <param name="expected">The accessible name required.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    /// <remarks>
    /// The accessible name is what a screen reader announces, which is not always the element's
    /// text: a label, an <c>aria-label</c>, or an image's alt text can supply it instead.
    /// </remarks>
    public Task ToHaveAccessibleNameAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var name = await _locator.GetAccessibilityNameAsync(ct).ConfigureAwait(false) ?? "";
            return (name == expected, name);
        }, "ToHaveAccessibleName", expected, options);

    /// <summary>Asserts that the element's computed ARIA role equals the expected role.</summary>
    /// <param name="expected">The ARIA role required, such as <c>button</c> or <c>navigation</c>.</param>
    /// <param name="options">Timeout and failure message overrides.</param>
    /// <remarks>
    /// The role is the computed one, so an element carries its implicit role without an explicit
    /// <c>role</c> attribute: a <c>&lt;nav&gt;</c> reports <c>navigation</c>.
    /// </remarks>
    public Task ToHaveRoleAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var role = await _locator.GetAccessibilityRoleAsync(ct).ConfigureAwait(false) ?? "";
            return (role == expected, role);
        }, "ToHaveRole", expected, options);
}
