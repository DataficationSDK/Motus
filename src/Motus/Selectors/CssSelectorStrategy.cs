using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in CSS selector strategy. Prefix: css=
/// </summary>
internal sealed class CssSelectorStrategy : ISelectorStrategy
{
    private readonly bool _pierceShadow;

    internal CssSelectorStrategy(bool pierceShadow = false)
    {
        _pierceShadow = pierceShadow;
    }

    public string StrategyName => "css";

    public int Priority => 10;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);
        var escaped = JsonEncodedText.Encode(selector).ToString();
        var js = $"""Array.from(document.querySelectorAll("{escaped}"))""";
        return await SelectorStrategyHelpers.EvalToHandlesAsync(page, js, ct);
    }

    public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
    {
        var result = await element.EvaluateAsync<string?>(
            """
            function() {
                var el = this;
                if (el.id) return '#' + CSS.escape(el.id);
                var parts = [];
                while (el && el !== document.body && el !== document.documentElement) {
                    var tag = el.tagName.toLowerCase();
                    if (el.id) { parts.unshift('#' + CSS.escape(el.id)); break; }
                    var classes = Array.from(el.classList).map(c => '.' + CSS.escape(c)).join('');
                    if (classes) { parts.unshift(tag + classes); }
                    else {
                        var parent = el.parentElement;
                        if (parent) {
                            var siblings = Array.from(parent.children).filter(c => c.tagName === el.tagName);
                            if (siblings.length > 1) {
                                var idx = siblings.indexOf(el) + 1;
                                parts.unshift(tag + ':nth-of-type(' + idx + ')');
                            } else { parts.unshift(tag); }
                        } else { parts.unshift(tag); }
                    }
                    el = el.parentElement;
                }
                return 'css=' + parts.join(' > ');
            }
            """);
        return result;
    }
}
