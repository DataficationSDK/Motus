using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in text selector strategy. Prefix: text=
/// Supports exact match with double quotes: text="exact text"
/// </summary>
internal sealed class TextSelectorStrategy : ISelectorStrategy
{
    public string StrategyName => "text";

    public int Priority => 20;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);

        var rawSelector = selector.StartsWith("text=", StringComparison.Ordinal) ? selector[5..] : selector;
        var isExact = rawSelector.Length >= 2 && rawSelector[0] == '"' && rawSelector[^1] == '"';
        var text = isExact ? rawSelector[1..^1] : rawSelector;
        var escaped = JsonEncodedText.Encode(text).ToString();
        var matchExpr = isExact
            ? $"""el.textContent&&el.textContent.trim()==="{escaped}" """
            : $"""el.textContent&&el.textContent.includes("{escaped}")""";

        var js = pierceShadow
            ? $$"""
                (()=>{
                    function walkShadow(root){
                        var results=[];
                        var all=root.querySelectorAll('*');
                        for(var i=0;i<all.length;i++){
                            var el=all[i];
                            if({{matchExpr}}) results.push(el);
                            var sr=el.shadowRoot;
                            if(sr) results=results.concat(walkShadow(sr));
                        }
                        return results;
                    }
                    return walkShadow(document);
                })()
                """
            : $$"""
                (()=>{
                    var results=[];
                    var walker=document.createTreeWalker(document.body,NodeFilter.SHOW_ELEMENT);
                    var el;
                    while(el=walker.nextNode()){
                        if({{matchExpr}}) results.push(el);
                    }
                    return results;
                })()
                """;

        return await SelectorStrategyHelpers.EvalToHandlesAsync(page, js, ct).ConfigureAwait(false);
    }

    public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
    {
        var text = await element.TextContentAsync(ct).ConfigureAwait(false);
        if (text is null || text.Length > 100)
            return null;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return null;

        return "text=" + trimmed;
    }
}
