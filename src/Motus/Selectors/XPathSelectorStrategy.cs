using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in XPath selector strategy. Prefix: xpath=
/// </summary>
internal sealed class XPathSelectorStrategy : ISelectorStrategy
{
    public string StrategyName => "xpath";

    public int Priority => 10;

    // XPath cannot traverse shadow DOM boundaries (language limitation)
    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);
        var xpathExpr = selector.StartsWith("xpath=", StringComparison.Ordinal) ? selector[6..] : selector;
        var escaped = JsonEncodedText.Encode(xpathExpr).ToString();
        var js = $$"""
            (()=>{
                var r=document.evaluate("{{escaped}}",document,null,XPathResult.ORDERED_NODE_SNAPSHOT_TYPE,null);
                var a=[];
                for(var i=0;i<r.snapshotLength;i++) a.push(r.snapshotItem(i));
                return a;
            })()
            """;
        return await SelectorStrategyHelpers.EvalToHandlesAsync(page, js, ct).ConfigureAwait(false);
    }

    public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
    {
        var result = await element.EvaluateAsync<string?>(
            """
            function() {
                var el = this;
                var parts = [];
                while (el && el.nodeType === Node.ELEMENT_NODE) {
                    if (el === document.body) { parts.unshift('/html/body'); break; }
                    var tag = el.tagName.toLowerCase();
                    var parent = el.parentNode;
                    if (parent) {
                        var siblings = Array.from(parent.children).filter(c => c.tagName === el.tagName);
                        var idx = siblings.indexOf(el) + 1;
                        parts.unshift('/' + tag + '[' + idx + ']');
                    } else { parts.unshift('/' + tag); }
                    el = el.parentNode;
                }
                return 'xpath=' + parts.join('');
            }
            """).ConfigureAwait(false);
        return result;
    }
}
