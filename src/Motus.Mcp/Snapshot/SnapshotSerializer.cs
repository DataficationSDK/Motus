using System.Text;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Where a ref points: a backend DOM node, and the frame whose document holds it. A null frame
/// means the document the snapshot itself covered, which is the page unless the snapshot was
/// scoped.
/// </summary>
/// <remarks>
/// A node identifier only means anything to the document it was read from, so a ref that names an
/// element inside a frame has to carry that frame with it. Otherwise the same number would address
/// a different element, or nothing, as soon as the session looked somewhere else.
/// </remarks>
internal sealed record RefTarget(long BackendNodeId, IFrame? Frame = null);

/// <summary>
/// The serialized form of an accessibility snapshot: the indented text the agent
/// reads, the map from each assigned ref to the element it addresses, and the
/// frames that were printed inside it.
/// </summary>
internal sealed record SerializedSnapshot(
    string Text,
    IReadOnlyDictionary<string, RefTarget> Refs,
    IReadOnlyList<FrameTree> Frames);

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
    /// <param name="roots">The roots of the document to render.</param>
    /// <param name="maxDepth">How many printed levels below each root to render, or null for all.</param>
    /// <param name="frames">
    /// The frame documents to print inside this one, found by the element that hosts each. Null
    /// prints each frame element as a leaf, which is what a snapshot already scoped to one frame
    /// wants. A frame counts as a level of the tree like anything else, so a depth limit stops at a
    /// frame boundary just as it stops anywhere else.
    /// </param>
    public static SerializedSnapshot Serialize(
        IReadOnlyList<AccessibilityNode> roots, int? maxDepth, InlineFrames? frames = null)
    {
        ArgumentNullException.ThrowIfNull(roots);

        var writer = new Writer(maxDepth, frames);
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

    private static bool IsDocument(AccessibilityNode node)
        => string.Equals(node.Role, "RootWebArea", StringComparison.OrdinalIgnoreCase);

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
    private sealed class Writer(int? maxDepth, InlineFrames? frames)
    {
        private readonly StringBuilder _text = new();
        private readonly Dictionary<string, RefTarget> _refs = new(StringComparer.Ordinal);
        private readonly Dictionary<AccessibilityNode, IReadOnlyList<AccessibilityNode>> _children =
            new(ReferenceEqualityComparer.Instance);
        private int _nextRef = 1;

        // The document being written, and the prefix its refs carry. The document the snapshot was
        // asked for is unprefixed and needs no frame recorded against its refs; each frame printed
        // inside it numbers its own refs from one behind its index, so f1e2 inside the page reads
        // the same as e2 does in a snapshot scoped to frame 1.
        private IFrame? _frame;
        private string _prefix = "";
        private readonly List<FrameTree> _frames = [];

        public SerializedSnapshot Finish() => new(_text.ToString(), _refs, _frames);

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
                var refId = $"{_prefix}e{_nextRef++}";
                _refs[refId] = new RefTarget(node.BackendDOMNodeId!.Value, _frame);
                _text.Append(" [ref=").Append(refId).Append(']');
            }

            // A frame element is the one place where the printed tree crosses into another
            // document, so it names the index the frame tools use for it. That index is also the
            // prefix on every ref inside, which is how an agent reads f1e2 back to a frame.
            var inner = FrameUnder(node);
            if (inner is not null)
                _text.Append(" [frame=").Append(inner.Index).Append(']');

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

            if (inner is null && remaining.Count > 0 && remaining.TrueForAll(IsStaticText))
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

            if (inner is not null)
                WriteFrame(inner, depth + 1);
        }

        /// <summary>
        /// The frame document the given element hosts, or null when it hosts none that was
        /// gathered: a frame past the cap, one whose tree could not be read, or any element at all
        /// when the caller asked for no frames.
        /// </summary>
        private FrameTree? FrameUnder(AccessibilityNode node)
            => frames is not null && node.BackendDOMNodeId is { } id ? frames.Find(_frame, id) : null;

        /// <summary>
        /// Writes a frame's document where the element that hosts it stands, numbering its refs
        /// from one again behind the frame's own prefix.
        /// </summary>
        private void WriteFrame(FrameTree tree, int depth)
        {
            if (maxDepth is { } limit && depth > limit)
                return;

            var outerFrame = _frame;
            var outerPrefix = _prefix;
            var outerNextRef = _nextRef;

            _frame = tree.Frame;
            _prefix = "f" + tree.Index;
            _nextRef = 1;
            _frames.Add(tree);

            // The frame's document node is dropped: that a second document starts here is what the
            // element hosting it has just said, and repeating it would cost a line and a level of
            // indentation on every frame on the page.
            if (tree.Roots.Count == 1 && IsDocument(tree.Roots[0]))
            {
                foreach (var child in ChildrenOf(tree.Roots[0]))
                    Write(child, depth);
            }
            else
            {
                foreach (var root in tree.Roots)
                {
                    if (Resolve(root) is { } node)
                        Write(node, depth);
                }
            }

            _frame = outerFrame;
            _prefix = outerPrefix;
            _nextRef = outerNextRef;
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
