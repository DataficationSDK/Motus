using System.Text;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// The serialized form of an accessibility snapshot: the indented text the agent
/// reads, paired with the map from each assigned ref to the backend DOM node it
/// addresses.
/// </summary>
internal sealed record SerializedSnapshot(
    string Text,
    IReadOnlyDictionary<string, long> RefToBackendNodeId);

/// <summary>
/// Renders an <see cref="AccessibilitySnapshot"/> into a compact, indented ARIA
/// text tree and assigns each node worth addressing a ref (e1, e2, ...) in document
/// order. Pure and stateless: the same snapshot always renders identically.
/// </summary>
/// <remarks>
/// The browser's tree says most things three times: a button, the static text inside it, and
/// the inline text box that text is laid out in. The agent needs the button. So the layout nodes
/// are never printed, text that only repeats its parent's name is folded away, text that adds
/// something is printed inline on the parent's line, and an unnamed wrapper with one child steps
/// aside for that child. Refs go only to nodes an agent might act on: interactive roles, named
/// nodes, frames, and anything focusable. Everything else is printed for context and nothing more.
/// </remarks>
internal static class SnapshotSerializer
{
    // Boolean accessibility properties surfaced as [state] flags, in render order.
    private static readonly string[] StateProperties =
        ["disabled", "readonly", "required", "checked", "selected", "expanded", "pressed"];

    // Roles an agent acts on. A node with one of these takes a ref even without a name, because an
    // unlabeled control is exactly what an audit needs to point at and a fix needs to reach.
    private static readonly HashSet<string> InteractiveRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "button", "link", "textbox", "searchbox", "combobox", "checkbox", "radio", "switch",
        "slider", "spinbutton", "menuitem", "menuitemcheckbox", "menuitemradio", "tab", "option",
        "listbox", "menu", "tree", "treeitem", "gridcell", "cell", "row", "Iframe",
    };

    // Text and label nodes never take a ref: the element they belong to is the thing to address.
    private static readonly HashSet<string> TextRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "StaticText", "InlineTextBox", "ListMarker", "LabelText",
    };

    // Layout detail with nothing to say. An inline text box is the line-broken layout of the
    // static text above it, and a list marker is implied by the list item it decorates.
    private static readonly HashSet<string> DroppedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "InlineTextBox", "ListMarker",
    };

    // The longest inline text the audit reports for a node before it is cut short.
    private const int InlineTextLimit = 80;

    public static SerializedSnapshot Serialize(AccessibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Serialize(snapshot.Roots, maxDepth: null);
    }

    /// <summary>
    /// Renders the given root nodes, optionally limiting how deep the tree is
    /// walked. <paramref name="maxDepth"/> counts printed levels below each root: 0
    /// renders only the roots, 1 adds their direct children, and null is unbounded.
    /// A wrapper that collapses into its child takes no level of its own. Refs are
    /// assigned only to nodes that are rendered.
    /// </summary>
    public static SerializedSnapshot Serialize(IReadOnlyList<AccessibilityNode> roots, int? maxDepth)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var writer = new Writer(maxDepth);
        foreach (var root in roots)
        {
            if (writer.Resolve(root) is { } node)
                writer.Write(node, depth: 0);
        }

        return writer.Finish();
    }

    /// <summary>
    /// Finds the first node with the given backend DOM node id, searching the
    /// roots in document order, or null if none carries it.
    /// </summary>
    public static AccessibilityNode? FindByBackendId(IReadOnlyList<AccessibilityNode> roots, long backendNodeId)
    {
        ArgumentNullException.ThrowIfNull(roots);

        foreach (var root in roots)
        {
            var match = FindByBackendId(root, backendNodeId);
            if (match is not null)
                return match;
        }

        return null;
    }

    /// <summary>
    /// The text under a node, as the snapshot would print it: every static text descendant in
    /// document order, whitespace normalized, cut short past a readable length. Null when the
    /// node has no text at all. This is how a violation on a node that took no ref is described.
    /// </summary>
    public static string? InlineText(AccessibilityNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var builder = new StringBuilder();
        CollectText(node, builder);
        if (builder.Length == 0)
            return null;

        return builder.Length <= InlineTextLimit
            ? builder.ToString()
            : builder.ToString(0, InlineTextLimit).TrimEnd() + "...";
    }

    /// <summary>
    /// Whether the node takes a ref: it maps to a DOM node, it is not itself text, and it is
    /// interactive, named, or focusable.
    /// </summary>
    internal static bool TakesRef(AccessibilityNode node)
    {
        if (node.BackendDOMNodeId is null)
            return false;

        var role = RoleOf(node);
        if (TextRoles.Contains(role))
            return false;

        return InteractiveRoles.Contains(role)
            || !string.IsNullOrWhiteSpace(node.Name)
            || IsTrue(node, "focusable");
    }

    private static void CollectText(AccessibilityNode node, StringBuilder builder)
    {
        if (IsStaticText(node))
        {
            var text = Normalize(node.Name);
            if (text.Length == 0)
                return;

            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(text);
            return;
        }

        foreach (var child in node.Children)
        {
            if (builder.Length > InlineTextLimit)
                return;
            CollectText(child, builder);
        }
    }

    private static AccessibilityNode? FindByBackendId(AccessibilityNode node, long backendNodeId)
    {
        if (node.BackendDOMNodeId == backendNodeId)
            return node;

        foreach (var child in node.Children)
        {
            var match = FindByBackendId(child, backendNodeId);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static string RoleOf(AccessibilityNode node)
        => string.IsNullOrEmpty(node.Role) ? "generic" : node.Role;

    private static bool IsStaticText(AccessibilityNode node)
        => string.Equals(node.Role, "StaticText", StringComparison.OrdinalIgnoreCase);

    private static bool IsGeneric(AccessibilityNode node)
        => string.IsNullOrEmpty(node.Role)
            || string.Equals(node.Role, "generic", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.Role, "none", StringComparison.OrdinalIgnoreCase)
            || string.Equals(node.Role, "presentation", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrue(AccessibilityNode node, string property)
        => node.Properties.TryGetValue(property, out var value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string? Property(AccessibilityNode node, string property)
        => node.Properties.TryGetValue(property, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    /// <summary>
    /// Collapses runs of whitespace, newlines included, to one space and trims the ends, so a
    /// name or text always fits on the one line the format gives it.
    /// </summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";

        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Escapes a value for printing between double quotes, so a name that itself contains a
    /// quote cannot be mistaken for the end of the name.
    /// </summary>
    private static string Escape(string text)
        => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>
    /// One rendering pass: the text and ref map being built, plus a memo of each node's children
    /// after dropping and collapsing, so a wrapper chain is resolved once rather than once per
    /// level it is looked at from.
    /// </summary>
    private sealed class Writer(int? maxDepth)
    {
        private readonly StringBuilder _text = new();
        private readonly Dictionary<string, long> _refs = new(StringComparer.Ordinal);
        private readonly Dictionary<AccessibilityNode, IReadOnlyList<AccessibilityNode>> _children =
            new(ReferenceEqualityComparer.Instance);
        private int _nextRef = 1;

        public SerializedSnapshot Finish() => new(_text.ToString(), _refs);

        /// <summary>
        /// The node as it will be printed: the node itself, the one child an unnamed wrapper
        /// steps aside for, or null for a node that prints nothing at all.
        /// </summary>
        public AccessibilityNode? Resolve(AccessibilityNode node)
        {
            if (DroppedRoles.Contains(RoleOf(node)))
                return null;

            if (!IsCollapsible(node))
                return node;

            var children = ChildrenOf(node);
            return children.Count switch
            {
                0 => null,
                1 => children[0],
                _ => node,
            };
        }

        public void Write(AccessibilityNode node, int depth)
        {
            if (maxDepth is { } limit && depth > limit)
                return;

            if (IsStaticText(node))
            {
                // Text reaches here only when it stands beside elements, or is a root of its
                // own. Text that is a node's only content is printed on that node's line instead.
                var text = Normalize(node.Name);
                if (text.Length > 0)
                    _text.Append(' ', depth * 2).Append("- text: ").Append(text).Append('\n');
                return;
            }

            _text.Append(' ', depth * 2).Append("- ").Append(RoleOf(node));

            var name = Normalize(node.Name);
            if (name.Length > 0)
                _text.Append(" \"").Append(Escape(name)).Append('"');

            if (TakesRef(node))
            {
                var refId = $"e{_nextRef++}";
                _refs[refId] = node.BackendDOMNodeId!.Value;
                _text.Append(" [ref=").Append(refId).Append(']');
            }

            // List items carry a level too, but it says how deep the list is nested, which the
            // indentation already shows. A heading's level is its outline rank, which nothing
            // else in the printout conveys.
            if (string.Equals(node.Role, "heading", StringComparison.OrdinalIgnoreCase)
                && Property(node, "level") is { } level)
            {
                _text.Append(" [level=").Append(level).Append(']');
            }

            if (string.Equals(node.Role, "link", StringComparison.OrdinalIgnoreCase)
                && Property(node, "url") is { } url)
            {
                _text.Append(" [url=").Append(url).Append(']');
            }

            var value = Normalize(node.Value);
            if (value.Length > 0)
                _text.Append(" [value=\"").Append(Escape(value)).Append("\"]");

            foreach (var state in StateProperties)
            {
                if (IsTrue(node, state))
                    _text.Append(" [").Append(state).Append(']');
            }

            // Text that only repeats the node's own name is the browser's way of saying where
            // the name came from, and printing it again would say nothing new.
            var remaining = new List<AccessibilityNode>();
            foreach (var child in ChildrenOf(node))
            {
                if (IsStaticText(child))
                {
                    var text = Normalize(child.Name);
                    if (text.Length == 0 || text == name)
                        continue;
                }

                remaining.Add(child);
            }

            if (remaining.Count > 0 && remaining.TrueForAll(IsStaticText))
            {
                var joined = string.Join(" ", remaining.Select(child => Normalize(child.Name)));
                if (joined != name)
                    _text.Append(": ").Append(joined);
                _text.Append('\n');
                return;
            }

            _text.Append('\n');
            foreach (var child in remaining)
                Write(child, depth + 1);
        }

        private IReadOnlyList<AccessibilityNode> ChildrenOf(AccessibilityNode node)
        {
            if (_children.TryGetValue(node, out var cached))
                return cached;

            var resolved = new List<AccessibilityNode>(node.Children.Count);
            foreach (var child in node.Children)
            {
                if (Resolve(child) is { } kept)
                    resolved.Add(kept);
            }

            _children[node] = resolved;
            return resolved;
        }

        /// <summary>
        /// An unnamed wrapper with nothing of its own to print: no ref, no value, no state. Such a
        /// node exists for layout, and the printout is about what is on the page, not how it is
        /// laid out.
        /// </summary>
        private static bool IsCollapsible(AccessibilityNode node)
        {
            if (!IsGeneric(node) || !string.IsNullOrWhiteSpace(node.Name) || TakesRef(node))
                return false;

            if (!string.IsNullOrWhiteSpace(node.Value))
                return false;

            foreach (var state in StateProperties)
            {
                if (IsTrue(node, state))
                    return false;
            }

            return true;
        }
    }
}
