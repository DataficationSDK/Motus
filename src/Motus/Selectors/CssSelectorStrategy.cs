using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in CSS selector strategy. Prefix: css=
/// </summary>
internal sealed class CssSelectorStrategy : ISelectorStrategy
{
    public string StrategyName => "css";

    public int Priority => 10;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);
        var cssSelector = selector.StartsWith("css=", StringComparison.Ordinal) ? selector[4..] : selector;
        var escaped = JsonEncodedText.Encode(cssSelector).ToString();

        var js = pierceShadow
            ? $$"""
                (()=>{
                    function queryShadow(root,sel){
                        var results=Array.from(root.querySelectorAll(sel));
                        var all=root.querySelectorAll('*');
                        for(var i=0;i<all.length;i++){
                            var sr=all[i].shadowRoot;
                            if(sr) results=results.concat(queryShadow(sr,sel));
                        }
                        return results;
                    }
                    return queryShadow(document,"{{escaped}}");
                })()
                """
            : $"""Array.from(document.querySelectorAll("{escaped}"))""";

        return await SelectorStrategyHelpers.EvalToHandlesAsync(page, js, ct).ConfigureAwait(false);
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
            """).ConfigureAwait(false);
        return result;
    }
}
