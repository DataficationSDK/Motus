using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// A frame a page snapshot printed inside itself: the index the frame tools address it by, the
/// frame, and the address it held when its tree was read.
/// </summary>
public sealed record SnapshotFrame(int Index, IFrame Frame, string Url);

/// <summary>
/// Holds the most recent accessibility snapshot for a single page and turns the targets a caller
/// names into actionable locators: the refs it assigned, and selectors, which need no snapshot at
/// all. Refs are valid only for the latest snapshot; taking a new snapshot replaces the ref map.
/// </summary>
/// <remarks>
/// A page snapshot prints the frames the page hosts inside it, so a ref can name an element in any
/// of them. Such a ref carries the frame it came from (<c>f1e2</c> is the second element of frame
/// one) and is resolved through that frame whatever the session is scoped to at the time.
/// </remarks>
public sealed class PageSnapshotService
{
    private readonly IPage _page;
    private IReadOnlyDictionary<string, RefTarget>? _refs;
    private IReadOnlyDictionary<long, string>? _backendNodeIdToRef;
    private IReadOnlyList<AccessibilityNode>? _roots;
    private IFrame? _refFrame;

    public PageSnapshotService(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        _page = page;
    }

    /// <summary>The text of the most recent snapshot, or null if none has been taken.</summary>
    public string? LastSnapshot { get; private set; }

    /// <summary>
    /// The frames the most recent snapshot printed inside the page, each with the address it held
    /// when it was read. Empty when the snapshot covered no frames.
    /// </summary>
    /// <remarks>
    /// Kept so that an action can say when a frame has navigated out from under the refs the agent
    /// is holding. A frame can go somewhere else without the page moving at all, and nothing in the
    /// page's own address would show it.
    /// </remarks>
    public IReadOnlyList<SnapshotFrame> InlinedFrames { get; private set; } = [];

    /// <summary>
    /// The frame the most recent snapshot described, or null when it described the page. A snapshot
    /// rooted at a ref inside a frame ends up scoped to that frame, so this is not always the frame
    /// the caller asked for.
    /// </summary>
    public IFrame? Scope => _refFrame;

    /// <summary>
    /// Fetches a fresh accessibility snapshot, assigns refs in document order, and
    /// returns the indented text representation. Replaces any earlier ref map.
    /// </summary>
    public Task<string> TakeSnapshotAsync(CancellationToken ct = default)
        => TakeSnapshotAsync(rootRef: null, maxDepth: null, ct);

    /// <summary>
    /// Fetches a fresh snapshot and renders it, optionally scoped. When
    /// <paramref name="rootRef"/> is given, the snapshot is rooted at the subtree
    /// of that ref (resolved against the previous snapshot's map). When
    /// <paramref name="maxDepth"/> is given, the tree is rendered only that many
    /// levels deep. Replaces the ref map with the refs assigned this call.
    /// </summary>
    /// <exception cref="SnapshotNotTakenException">
    /// <paramref name="rootRef"/> was given but no earlier snapshot exists to resolve it against.
    /// </exception>
    /// <exception cref="StaleRefException">
    /// <paramref name="rootRef"/> is not in the previous snapshot, or its element is no longer present.
    /// </exception>
    public Task<string> TakeSnapshotAsync(string? rootRef, int? maxDepth, CancellationToken ct = default)
        => TakeSnapshotAsync(scope: null, rootRef, maxDepth, ct);

    /// <summary>
    /// Fetches a fresh snapshot of one frame, or of the whole page when <paramref name="scope"/> is
    /// null, and renders it as above.
    /// </summary>
    public Task<string> TakeSnapshotAsync(
        IFrame? scope, string? rootRef, int? maxDepth, CancellationToken ct = default)
        => TakeSnapshotAsync(scope, rootRef, maxDepth, maxFrames: null, ct);

    /// <summary>
    /// Fetches a fresh snapshot and renders it as above, printing the frames the page hosts inside
    /// it up to <paramref name="maxFrames"/> of them.
    /// </summary>
    /// <param name="scope">The frame to describe, or null for the whole page.</param>
    /// <param name="rootRef">A ref from the previous snapshot to root this one at, or null.</param>
    /// <param name="maxDepth">How many levels of the tree to render, or null for all of it.</param>
    /// <param name="maxFrames">How many frames to print inside the page, or null for the default.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>
    /// The frame a ref came from is remembered alongside it, because a ref only means anything to
    /// the document that produced it. Resolving against whatever frame happens to be selected later
    /// would silently address a different document, or nothing at all.
    /// <para>
    /// A snapshot of one frame describes that frame and stops there, so its refs read as plain
    /// numbers exactly as they did before frames were printed inline. Only a snapshot of the page
    /// reaches into frames, and the refs it hands out there name the frame they came from.
    /// </para>
    /// </remarks>
    public async Task<string> TakeSnapshotAsync(
        IFrame? scope, string? rootRef, int? maxDepth, int? maxFrames, CancellationToken ct = default)
    {
        RefTarget? root = null;
        if (rootRef is not null)
        {
            if (_refs is null)
                throw new SnapshotNotTakenException();

            if (!_refs.TryGetValue(rootRef, out var target))
                throw new StaleRefException(rootRef);

            root = target;
        }

        // A ref that named an element inside a frame roots the snapshot in that frame's document,
        // which is also what scopes it: the tree that comes back is the frame's, so the refs handed
        // out for it are the frame's own and carry no prefix.
        scope = root?.Frame ?? scope;

        var snapshot = scope is null
            ? await _page.AccessibilitySnapshotAsync(ct).ConfigureAwait(false)
            : await scope.AccessibilitySnapshotAsync(ct).ConfigureAwait(false);

        var roots = snapshot.Roots;
        if (root is { } target2)
        {
            roots = [SnapshotSerializer.FindByBackendId(snapshot.Roots, target2.BackendNodeId)
                ?? throw new StaleRefException(rootRef!)];
        }

        // Only a page snapshot reaches into frames. Inside one frame the caller has already said
        // which document it is looking at, and a second level of prefixed refs there would name
        // frames by an index the printout never showed.
        var frames = scope is null
            ? await FrameCollector.CollectAsync(
                _page, roots, maxFrames ?? FrameCollector.DefaultMaxFrames, ct).ConfigureAwait(false)
            : null;

        var serialized = SnapshotSerializer.Serialize(roots, maxDepth, frames);

        _refs = serialized.Refs;
        _backendNodeIdToRef = BuildReverseMap(serialized.Refs);
        _roots = snapshot.Roots;
        _refFrame = scope;
        InlinedFrames = [.. serialized.Frames.Select(f => new SnapshotFrame(f.Index, f.Frame, f.Frame.Url))];

        var text = serialized.Text;

        if (rootRef is null && serialized.Refs.Count == 0)
        {
            // The browser may be one that cannot produce an accessibility tree at all, in which
            // case the snapshot says why rather than guessing at the page. Blaming the page for
            // an empty tree that the browser was never going to fill sends the agent looking in
            // the wrong place, and refs are unavailable for every element, not just the ones a
            // canvas would hide.
            //
            // Otherwise a whole-page snapshot with nothing addressable usually does mean the app
            // paints to a canvas or custom surface rather than the DOM. Say so, and point at the
            // coordinate workflow, instead of letting the agent conclude the page is empty.
            text = text.TrimEnd('\n') + "\n\nNote: " + (snapshot.DiagnosticMessage is { Length: > 0 } diagnostic
                ? diagnostic
                  + " Snapshots address elements by ref, so the tools that take a ref cannot be used "
                  + "with this browser."
                : "no addressable elements were found. The page may render to a canvas or custom "
                  + "surface that the accessibility tree cannot describe.")
                + " Take a screenshot to identify controls visually, then act on their positions "
                + "with click_xy, drag, or scroll_xy.\n";
        }

        // A frame left out of the tree is one the agent cannot see at all, so say how many and
        // where to look. A frame is left out when the page has more of them than this snapshot
        // prints, when its element is hidden and so is not in the page's tree to print it under,
        // or when its own tree could not be read.
        var missing = _page.Frames.Count - 1 - serialized.Frames.Count;
        if (scope is null && missing > 0)
        {
            text = text.TrimEnd('\n')
                + $"\n\nNote: this page has {missing} more frame{(missing == 1 ? "" : "s")} whose contents are "
                + "not in this tree. Use frame_list to see them and frame_select to look inside one.\n";
        }

        LastSnapshot = text;
        return text;
    }

    private static Dictionary<long, string> BuildReverseMap(IReadOnlyDictionary<string, RefTarget> forward)
    {
        // Only the refs of the document the snapshot was asked for are inverted. The audit reads
        // this map to put a ref on a violation, and it audits that one document, so a node
        // identifier from a frame could only collide with one of its own.
        var reverse = new Dictionary<long, string>(forward.Count);
        foreach (var (refId, target) in forward)
        {
            if (target.Frame is null)
                reverse[target.BackendNodeId] = refId;
        }

        return reverse;
    }

    /// <summary>
    /// Resolves a target to a locator: a ref from the current snapshot, or a selector.
    /// The element is resolved lazily when an action runs on the returned locator; if it has
    /// since detached from the document, that action fails.
    /// </summary>
    /// <remarks>
    /// A ref is built against whatever the snapshot covered, not against whatever is selected now,
    /// so it keeps addressing the element it named even if the scope has moved on. Anything that is
    /// not shaped like a ref is handed to the engine's selector strategies, which cost no snapshot
    /// and are how a caller that already knows a stable selector says so. This overload searches a
    /// selector in the frame the last snapshot covered; the overload below takes that frame from
    /// the caller, which is what the tools do.
    /// </remarks>
    /// <exception cref="SnapshotNotTakenException">
    /// A ref was given and no snapshot has been taken yet.
    /// </exception>
    /// <exception cref="StaleRefException">The ref is not in the current snapshot.</exception>
    public ILocator ResolveRef(string refId) => Resolve(refId, selectorScope: _refFrame);

    /// <summary>
    /// Resolves a target as above, naming the frame a selector is searched in.
    /// </summary>
    /// <param name="refId">A ref from the current snapshot, or a selector.</param>
    /// <param name="scope">
    /// The frame selected now, or null for the page. A ref ignores it and keeps addressing the
    /// document its own snapshot covered.
    /// </param>
    /// <remarks>
    /// A selector describes an element rather than naming one, so it means whatever the session is
    /// looking at now: the frame selected at the moment of the call, not the frame some earlier
    /// snapshot happened to cover. A ref is the opposite, which is why the two read different
    /// frames here. It also means a selector works straight after <c>frame_select</c>, with no
    /// snapshot in between.
    /// </remarks>
    public ILocator ResolveRef(string refId, IFrame? scope) => Resolve(refId, selectorScope: scope);

    private ILocator Resolve(string refId, IFrame? selectorScope)
    {
        if (!IsRefShaped(refId))
        {
            return selectorScope is { } selectorFrame
                ? selectorFrame.Locator(refId)
                : _page.Locator(refId);
        }

        if (_refs is null)
            throw new SnapshotNotTakenException();

        if (!_refs.TryGetValue(refId, out var target))
            throw new StaleRefException(refId);

        // A ref that named an element inside a frame carries that frame, and is resolved through it
        // whatever the session is scoped to now. Everything else belongs to the document the
        // snapshot covered.
        var owner = target.Frame ?? _refFrame;
        return owner is { } frame
            ? frame.LocatorByBackendNodeId(target.BackendNodeId)
            : _page.LocatorByBackendNodeId(target.BackendNodeId);
    }

    /// <summary>
    /// Whether a target is shaped like a ref this service hands out (<c>e5</c>), rather than a
    /// selector. An optional frame prefix (<c>f1e5</c>) is recognized here so that refs naming the
    /// frame they came from read as refs the day they are assigned rather than run as selectors.
    /// </summary>
    /// <remarks>
    /// The shape decides the answer, not the ref map, so a ref the latest snapshot no longer holds
    /// is reported as stale instead of being sent to the browser as a selector that matches nothing.
    /// The shape is narrow on purpose: no CSS selector is a lone letter followed by digits.
    /// </remarks>
    private static bool IsRefShaped(string? target)
    {
        if (string.IsNullOrEmpty(target))
            return false;

        var span = target.AsSpan();

        if (span[0] == 'f')
        {
            var frameDigits = 1;
            while (frameDigits < span.Length && char.IsAsciiDigit(span[frameDigits]))
                frameDigits++;

            if (frameDigits == 1)
                return false;

            span = span[frameDigits..];
        }

        if (span.Length < 2 || span[0] != 'e')
            return false;

        foreach (var character in span[1..])
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns the ref the current snapshot assigned to the given backend DOM node,
    /// or null when no snapshot has been taken or the node was not assigned a ref.
    /// Refs go only to nodes worth targeting (interactive, named, focusable, or a
    /// frame), so an unnamed image or an empty landmark has none even though it is
    /// in the tree; <see cref="GetTextForNodeId"/> describes such a node instead.
    /// </summary>
    public string? GetRefForNodeId(long backendNodeId)
        => _backendNodeIdToRef is not null
            && _backendNodeIdToRef.TryGetValue(backendNodeId, out var refId)
            ? refId
            : null;

    /// <summary>
    /// Returns the text the current snapshot holds under the given backend DOM node,
    /// as the snapshot prints it, or null when no snapshot has been taken, the node
    /// is not in it, or it contains no text.
    /// </summary>
    public string? GetTextForNodeId(long backendNodeId)
        => _roots is not null && SnapshotSerializer.FindByBackendId(_roots, backendNodeId) is { } node
            ? SnapshotSerializer.InlineText(node)
            : null;
}
