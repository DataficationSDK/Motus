using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// The kind of match a <see cref="GetBySelectorStrategy"/> performs.
/// </summary>
internal enum GetByKind
{
    /// <summary>The innermost element whose text matches. Prefix: _text=</summary>
    Text,

    /// <summary>An element whose label matches. Prefix: _label=</summary>
    Label,

    /// <summary>An element whose named attribute matches. Prefix: _attr=</summary>
    Attribute,
}

/// <summary>
/// Resolves the text-based locator factories (<c>GetByText</c>, <c>GetByLabel</c>,
/// <c>GetByPlaceholder</c>, <c>GetByTitle</c>, <c>GetByAltText</c>) in the page.
/// </summary>
/// <remarks>
/// <para>
/// The expression carries the text as a JSON string literal followed by a one-letter flag:
/// <c>i</c> for a case-insensitive substring match and <c>s</c> for a case-sensitive match of the
/// whole string. Whitespace is collapsed and trimmed on both sides before either comparison, so
/// markup indentation never decides a match. The attribute kind puts the attribute name in front:
/// <c>_attr=placeholder="Search"i</c>. Quoting the text as JSON means any quote, bracket or
/// backslash in it survives into the query unchanged.
/// </para>
/// <para>
/// The prefixes start with an underscore, like <c>_node</c>, to mark them as reserved for the
/// locator factories. They resolve like any other selector, and never generate one for the
/// recorder.
/// </para>
/// </remarks>
internal sealed class GetBySelectorStrategy : ISelectorStrategy
{
    internal const string TextPrefix = "_text";
    internal const string LabelPrefix = "_label";
    internal const string AttributePrefix = "_attr";

    private readonly GetByKind _kind;

    internal GetBySelectorStrategy(GetByKind kind)
    {
        _kind = kind;
    }

    public string StrategyName => _kind switch
    {
        GetByKind.Text => TextPrefix,
        GetByKind.Label => LabelPrefix,
        _ => AttributePrefix,
    };

    // Never generates a selector, so its place in the recorder's ordering is moot.
    public int Priority => 0;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var expression = selector.StartsWith(StrategyName + "=", StringComparison.Ordinal)
            ? selector[(StrategyName.Length + 1)..]
            : selector;

        var (attribute, text, exact) = ParseExpression(expression, _kind == GetByKind.Attribute);

        var js = $$"""
            (()=>{
                var kind="{{_kind.ToString().ToLowerInvariant()}}";
                var attr="{{JsonEncodedText.Encode(attribute ?? "")}}";
                var exact={{(exact ? "true" : "false")}};
                var pierce={{(pierceShadow ? "true" : "false")}};
                function norm(s){return String(s).replace(/\s+/g,' ').trim();}
                var want=norm("{{JsonEncodedText.Encode(text)}}");
                var wantLower=want.toLowerCase();
                function matches(s){
                    if(s==null) return false;
                    s=norm(s);
                    return exact ? s===want : s.toLowerCase().indexOf(wantLower)!==-1;
                }
                var SKIP={SCRIPT:1,STYLE:1,NOSCRIPT:1,TEMPLATE:1};
                var cache=new Map();
                function textOf(node){
                    if(cache.has(node)) return cache.get(node);
                    var s='';
                    for(var c=node.firstChild;c;c=c.nextSibling){
                        if(c.nodeType===3) s+=c.nodeValue;
                        else if(c.nodeType===1 && !SKIP[c.nodeName]) s+=textOf(c);
                    }
                    if(pierce && node.shadowRoot) s+=textOf(node.shadowRoot);
                    cache.set(node,s);
                    return s;
                }
                var all=[];
                function collect(root){
                    var els=root.querySelectorAll('*');
                    for(var i=0;i<els.length;i++){
                        all.push(els[i]);
                        if(pierce && els[i].shadowRoot) collect(els[i].shadowRoot);
                    }
                }
                collect(document);
                function isValueButton(el){
                    if(el.nodeName!=='INPUT') return false;
                    var t=(el.getAttribute('type')||'').toLowerCase();
                    return t==='button'||t==='submit'||t==='reset';
                }
                function textMatches(el){
                    if(SKIP[el.nodeName] || (document.head && document.head.contains(el))) return false;
                    if(isValueButton(el)) return matches(el.value);
                    if(!matches(textOf(el))) return false;
                    var kids=Array.from(el.children);
                    if(pierce && el.shadowRoot) kids=kids.concat(Array.from(el.shadowRoot.children));
                    for(var i=0;i<kids.length;i++){
                        if(!SKIP[kids[i].nodeName] && matches(textOf(kids[i]))) return false;
                    }
                    return true;
                }
                function labelsOf(el){
                    var ids=(el.getAttribute('aria-labelledby')||'').split(/\s+/).filter(Boolean);
                    if(ids.length){
                        var root=el.getRootNode();
                        var parts=[];
                        for(var i=0;i<ids.length;i++){
                            var ref=root.getElementById ? root.getElementById(ids[i]) : document.getElementById(ids[i]);
                            if(ref) parts.push(textOf(ref));
                        }
                        if(parts.length) return parts.length>1 ? parts.concat([parts.join(' ')]) : parts;
                    }
                    var aria=el.getAttribute('aria-label');
                    if(aria && aria.trim()) return [aria];
                    if(el.labels && !(el.nodeName==='INPUT' && el.type==='hidden'))
                        return Array.from(el.labels).map(textOf);
                    return [];
                }
                return all.filter(function(el){
                    if(kind==='text') return textMatches(el);
                    if(kind==='label') return labelsOf(el).some(matches);
                    return el.hasAttribute(attr) && matches(el.getAttribute(attr));
                });
            })()
            """;

        return await SelectorStrategyHelpers.EvalToHandlesAsync(frame, js, ct).ConfigureAwait(false);
    }

    public Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    /// <summary>
    /// Formats the expression for a text and match mode: the text as a JSON string literal,
    /// followed by <c>s</c> for an exact match or <c>i</c> otherwise.
    /// </summary>
    internal static string FormatText(string text, bool exact)
        => "\"" + JsonEncodedText.Encode(text, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping)
           + "\"" + (exact ? "s" : "i");

    /// <summary>
    /// Splits an expression built by <see cref="FormatText"/>, with an attribute name in front of
    /// it when <paramref name="hasAttribute"/> is set, back into its parts.
    /// </summary>
    /// <exception cref="InvalidOperationException">The expression is not in that form.</exception>
    internal static (string? attribute, string text, bool exact) ParseExpression(
        string expression, bool hasAttribute)
    {
        string? attribute = null;
        var literal = expression;

        if (hasAttribute)
        {
            var eq = expression.IndexOf('=');
            if (eq <= 0)
                throw Malformed(expression);
            attribute = expression[..eq];
            literal = expression[(eq + 1)..];
        }

        if (literal.Length < 3 || literal[0] != '"' || literal[^2] != '"' || literal[^1] is not ('i' or 's'))
            throw Malformed(expression);

        string? text;
        try
        {
            using var doc = JsonDocument.Parse(literal.AsMemory(0, literal.Length - 1));
            text = doc.RootElement.ValueKind == JsonValueKind.String ? doc.RootElement.GetString() : null;
        }
        catch (JsonException)
        {
            text = null;
        }

        if (text is null)
            throw Malformed(expression);

        return (attribute, text, literal[^1] == 's');
    }

    private static InvalidOperationException Malformed(string expression)
        => new($"Malformed locator expression '{expression}': expected a quoted string followed by 'i' or 's'.");
}
