using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// One frame's document, ready to be printed inside the tree of the document that hosts it: the
/// index the frame tools address the frame by, the frame itself, and the roots read from it.
/// </summary>
internal sealed record FrameTree(int Index, IFrame Frame, IReadOnlyList<AccessibilityNode> Roots);

/// <summary>
/// The frame documents a page snapshot prints inside itself, found by the element that hosts each
/// one.
/// </summary>
/// <remarks>
/// A frame element is identified by the document it sits in as well as by its node, because a node
/// identifier only means anything to the document it came from and two documents can hand out the
/// same one. A null document is the one the snapshot itself covered.
/// </remarks>
internal sealed class InlineFrames
{
    private readonly Dictionary<(IFrame? Host, long Node), FrameTree> _byHost = [];

    /// <summary>How many frame documents were gathered.</summary>
    public int Count => _byHost.Count;

    /// <summary>Records the frame the given element hosts.</summary>
    public void Add(IFrame? host, long hostNodeId, FrameTree tree)
        => _byHost[(host, hostNodeId)] = tree;

    /// <summary>
    /// The frame the given element hosts, or null when that element hosts no frame that was
    /// gathered.
    /// </summary>
    public FrameTree? Find(IFrame? host, long hostNodeId)
        => _byHost.TryGetValue((host, hostNodeId), out var tree) ? tree : null;
}
