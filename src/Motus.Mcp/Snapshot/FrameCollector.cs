using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Reads the documents of the frames a page hosts so the page snapshot can print them where they
/// belong, inside the element that hosts each.
/// </summary>
/// <remarks>
/// The browser does not hand them over with the page. Asked for the page's document it answers with
/// that document alone, and the element that hosts a frame comes back as a leaf whether the frame
/// is rendered in the page's own process or in one of its own, so every frame is read separately
/// and stitched in here.
/// <para>
/// Which frame belongs to which element is the awkward part. The engine lists a frame's children in
/// the order it learned about them, which is not the order they appear in the document, so counting
/// elements off against that list would put one frame's content under another frame's element. The
/// browsing context index settles it: the page can say which of its child contexts an element
/// holds, and each frame can say which of its parent's contexts it is, and those two numbers agree.
/// Both readings are allowed across origins. When either is unavailable the order the engine gives
/// is used instead, which is right whenever the page has one frame and a guess after that.
/// </para>
/// </remarks>
internal static class FrameCollector
{
    /// <summary>How many frames a page snapshot prints inside itself unless the caller says otherwise.</summary>
    internal const int DefaultMaxFrames = 10;

    // Which of its parent's child browsing contexts this document is. Comparing window identities
    // is one of the few things a document may do across origins, so this answers for a frame the
    // browser renders in its own process as readily as for one it does not.
    private const string SelfIndexExpression =
        "(() => { const p = window.parent; for (let i = 0; i < p.length; i++) { if (p[i] === window) return i; } return -1; })()";

    // Which child browsing context this element holds, asked of the document the element lives in.
    private const string HostIndexExpression =
        "el => { const w = el.ownerDocument.defaultView; for (let i = 0; i < w.length; i++) { if (w[i] === el.contentWindow) return i; } return -1; }";

    /// <summary>
    /// Reads up to <paramref name="maxFrames"/> of the page's frames, depth first in the order the
    /// documents print, and returns them keyed by the element that hosts each.
    /// </summary>
    /// <param name="page">The page whose frames to read.</param>
    /// <param name="roots">The roots of the page's own document, already read.</param>
    /// <param name="maxFrames">How many frames to read at most.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// A frame that cannot be read is left out rather than failing the snapshot: a frame can detach
    /// or navigate between the page's tree arriving and its own being asked for, and a page
    /// snapshot that reported nothing at all because one advertising frame went away would be worse
    /// than one missing that frame.
    /// </remarks>
    internal static async Task<InlineFrames> CollectAsync(
        IPage page,
        IReadOnlyList<AccessibilityNode> roots,
        int maxFrames,
        CancellationToken ct)
    {
        var collected = new InlineFrames();

        // The page's own flat list answers "are there any frames at all" without walking the tree,
        // which keeps a page that has none from paying anything for this at all.
        if (maxFrames <= 0 || page.Frames.Count <= 1)
            return collected;

        var indexOf = FrameIndexes(page);
        var claimed = new HashSet<IFrame>(ReferenceEqualityComparer.Instance);
        var budget = maxFrames;

        await AddAsync(host: null, page.MainFrame, roots).ConfigureAwait(false);
        return collected;

        async Task AddAsync(IFrame? host, IFrame document, IReadOnlyList<AccessibilityNode> documentRoots)
        {
            var children = document.ChildFrames;
            if (budget <= 0 || children.Count == 0)
                return;

            var elements = FrameElements(documentRoots);
            if (elements.Count == 0)
                return;

            var byContext = await ByContextIndexAsync(children).ConfigureAwait(false);

            foreach (var element in elements)
            {
                if (budget <= 0)
                    return;

                var match = await MatchAsync(document, element, byContext).ConfigureAwait(false);
                if (match.HostsNothing)
                    continue;

                // An element that could not be matched is paired with a frame that could not say
                // where it belongs, before one that could: a frame that answered for itself is
                // still waiting for the element that will claim it, and taking it here would put
                // two frames under each other's elements.
                var frame = match.Frame
                    ?? children.FirstOrDefault(candidate =>
                        !claimed.Contains(candidate) && !byContext.Values.Contains(candidate))
                    ?? children.FirstOrDefault(candidate => !claimed.Contains(candidate));

                if (frame is null || !claimed.Add(frame) || !indexOf.TryGetValue(frame, out var index))
                    continue;

                IReadOnlyList<AccessibilityNode> inner;
                try
                {
                    inner = (await frame.AccessibilitySnapshotAsync(ct).ConfigureAwait(false)).Roots;
                }
                catch (Exception) when (!ct.IsCancellationRequested)
                {
                    continue;
                }

                collected.Add(host, element, new FrameTree(index, frame, inner));
                budget--;

                await AddAsync(frame, frame, inner).ConfigureAwait(false);
            }
        }

        async Task<Match> MatchAsync(
            IFrame document, long element, IReadOnlyDictionary<int, IFrame> byContext)
        {
            int position;
            try
            {
                position = await document.LocatorByBackendNodeId(element)
                    .EvaluateWithElementAsync<int>(HostIndexExpression)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return Match.Unknown;
            }

            // An element the tree calls a frame but that holds no browsing context, such as one
            // that has just been removed, hosts nothing and must not be handed a frame by guess.
            if (position < 0)
                return Match.None;

            return byContext.TryGetValue(position, out var frame) && !claimed.Contains(frame)
                ? new Match(frame, HostsNothing: false)
                : Match.Unknown;
        }
    }

    /// <summary>
    /// The outcome of asking which frame an element hosts: the frame when both sides agreed, a
    /// blank when the element could not say, or a statement that it hosts nothing at all.
    /// </summary>
    private readonly record struct Match(IFrame? Frame, bool HostsNothing)
    {
        public static readonly Match Unknown = new(null, HostsNothing: false);
        public static readonly Match None = new(null, HostsNothing: true);
    }

    /// <summary>
    /// Each frame in the order the frame tools list them, which is the order its index refers to.
    /// </summary>
    /// <remarks>
    /// Walked from the main frame rather than read from the page's flat frame list, so that the
    /// numbering here is the numbering <c>frame_list</c> prints and <c>frame_select</c> takes.
    /// </remarks>
    private static Dictionary<IFrame, int> FrameIndexes(IPage page)
    {
        var indexes = new Dictionary<IFrame, int>(ReferenceEqualityComparer.Instance);
        Walk(page.MainFrame);
        return indexes;

        void Walk(IFrame frame)
        {
            indexes[frame] = indexes.Count;
            foreach (var child in frame.ChildFrames)
                Walk(child);
        }
    }

    /// <summary>
    /// The frames of the given document by the browsing context index each reports for itself.
    /// Frames that cannot answer, and any index two frames both claim, are left out, so a reading
    /// that is not trustworthy falls through to the order the engine gives instead of guessing.
    /// </summary>
    private static async Task<IReadOnlyDictionary<int, IFrame>> ByContextIndexAsync(
        IReadOnlyList<IFrame> children)
    {
        var byContext = new Dictionary<int, IFrame>();
        var duplicates = new HashSet<int>();

        foreach (var child in children)
        {
            int position;
            try
            {
                position = await child.EvaluateAsync<int>(SelfIndexExpression).ConfigureAwait(false);
            }
            catch (Exception)
            {
                continue;
            }

            if (position < 0)
                continue;

            if (!byContext.TryAdd(position, child))
                duplicates.Add(position);
        }

        foreach (var position in duplicates)
            byContext.Remove(position);

        return byContext;
    }

    /// <summary>
    /// The backend node ids of the frame elements in a document, in document order.
    /// </summary>
    private static List<long> FrameElements(IReadOnlyList<AccessibilityNode> roots)
    {
        var elements = new List<long>();
        foreach (var root in roots)
            Walk(root);

        return elements;

        void Walk(AccessibilityNode node)
        {
            if (string.Equals(node.Role, "Iframe", StringComparison.OrdinalIgnoreCase)
                && node.BackendDOMNodeId is { } id)
            {
                elements.Add(id);
            }

            foreach (var child in node.Children)
                Walk(child);
        }
    }
}
