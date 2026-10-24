using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

[Flags]
internal enum ActionabilityFlags
{
    None           = 0,
    Visible        = 1 << 0,
    Enabled        = 1 << 1,
    Stable         = 1 << 2,
    ReceivesEvents = 1 << 3,
    Editable       = 1 << 4,

    // Not a check that can fail: brings the element into view before the stability and hit tests,
    // which can only pass for an element the pointer can actually reach.
    ScrollIntoView = 1 << 5,
}

internal static class ActionabilityChecker
{
    internal const int PollingIntervalMs = 100;
    internal const double DefaultTimeoutMs = 30_000;

    /// <remarks>
    /// The session matters: a remote object id is only meaningful on the session that produced it,
    /// so an element inside a frame with a process of its own has to be checked over that frame's
    /// session. The page is still needed, but only to name the page in a timeout message.
    /// <paramref name="scrollIntoView"/> replaces the in-page scroll for a caller that knows more
    /// about where the element sits than the element's own document does. It answers whether the
    /// element is in view now, and the pass is retried while it is not.
    /// </remarks>
    internal static async Task<string> WaitForActionabilityAsync(
        Page page,
        IMotusSession session,
        Func<CancellationToken, Task<string>> resolveObjectId,
        ActionabilityFlags flags,
        string selector,
        CancellationToken ct,
        Func<string, CancellationToken, Task<bool>>? scrollIntoView = null)
    {
        string lastCheckName = "resolve";
        string? lastCheckDetail = null;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                lastCheckName = "resolve";
                lastCheckDetail = null;

                string objectId;
                try
                {
                    objectId = await resolveObjectId(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is InvalidOperationException or MotusSelectorException)
                {
                    await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    if (flags == ActionabilityFlags.None)
                        return objectId;

                    lastCheckName = "visible";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.Visible) && !await IsVisibleAsync(session, objectId, ct).ConfigureAwait(false))
                    {
                        lastCheckDetail = "element was found but is not visible (display:none, visibility:hidden, zero size, or opacity:0)";
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "enabled";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.Enabled) && !await IsEnabledAsync(session, objectId, ct).ConfigureAwait(false))
                    {
                        lastCheckDetail = "element was found but is disabled";
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "editable";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.Editable) && !await IsEditableAsync(session, objectId, ct).ConfigureAwait(false))
                    {
                        lastCheckDetail = "element was found but is not editable";
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    // Scrolled on every pass rather than once up front, because the layout can move
                    // while an earlier check is waiting and take the element back out of view.
                    lastCheckName = "scrollIntoView";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.ScrollIntoView))
                    {
                        if (scrollIntoView is null)
                        {
                            await ScrollIntoViewIfNeededAsync(session, objectId, ct).ConfigureAwait(false);
                        }
                        else if (!await scrollIntoView(objectId, ct).ConfigureAwait(false))
                        {
                            lastCheckDetail = "element was scrolled toward the viewport but its center is still outside it";
                            await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                            continue;
                        }
                    }

                    lastCheckName = "stable";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.Stable))
                    {
                        var stableResult = await MeasureStabilityAsync(session, objectId, ct).ConfigureAwait(false);
                        if (!stableResult.IsStable)
                        {
                            lastCheckDetail = $"element was located but kept drifting (last measured drift: {stableResult.Drift:F1}px across x/y/width/height over 50ms)";
                            await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                            continue;
                        }
                    }

                    lastCheckName = "receivesEvents";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.ReceivesEvents))
                    {
                        var hitsTarget = await ReceivesEventsAsync(session, objectId, ct).ConfigureAwait(false);
                        if (!hitsTarget)
                        {
                            var coveringTag = await GetCoveringElementInfoAsync(session, objectId, ct).ConfigureAwait(false);
                            lastCheckDetail = $"element was found but is covered at its center point by <{coveringTag}> which intercepts pointer events";
                            await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                            continue;
                        }
                    }

                    return objectId;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is InvalidOperationException or MotusSelectorException)
                {
                    // Element went stale during checks, retry from scratch
                    await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            var message = $"Actionability check '{lastCheckName}' timed out for selector '{selector}'.";
            if (lastCheckDetail is not null)
                message += $" {lastCheckDetail}";

            throw new ActionTimeoutException(
                selector, lastCheckName, elementState: lastCheckDetail, pageUrl: page.Url,
                timeoutDuration: TimeSpan.FromMilliseconds(DefaultTimeoutMs),
                message: message);
        }
    }

    private static async Task<bool> IsVisibleAsync(IMotusSession session, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(session, objectId,
            """
            function() {
                var style = window.getComputedStyle(this);
                if (style.display === 'none') return false;
                if (style.visibility === 'hidden') return false;
                if (parseFloat(style.opacity) === 0) return false;
                var r = this.getBoundingClientRect();
                return r.width > 0 && r.height > 0;
            }
            """, ct).ConfigureAwait(false);
    }

    private static async Task<bool> IsEnabledAsync(IMotusSession session, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(session, objectId,
            "function() { return !this.disabled && this.getAttribute('aria-disabled') !== 'true'; }",
            ct).ConfigureAwait(false);
    }

    private static async Task<bool> IsEditableAsync(IMotusSession session, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(session, objectId,
            """
            function() {
                if (this.contentEditable === 'true') return true;
                var role = this.getAttribute('role');
                if (role === 'textbox') return true;
                var tag = this.tagName.toLowerCase();
                if (tag === 'input' || tag === 'textarea') return !this.disabled && !this.readOnly;
                return false;
            }
            """, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Scrolls the element into view unless it is already there. Returns whether it scrolled.
    /// </summary>
    /// <remarks>
    /// Runs in the page rather than through <c>DOM.scrollIntoViewIfNeeded</c>, which only Chromium
    /// has. "Already there" means wholly inside the viewport and inside every ancestor that clips
    /// it, or, for an element too big to fit, centered inside them, since the center is where a
    /// pointer action aims. An element that is already in view is left exactly where it is, so a
    /// retried action does not make the page jump.
    /// </remarks>
    internal static async Task<bool> ScrollIntoViewIfNeededAsync(
        IMotusSession session, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(session, objectId, ScrollIntoViewIfNeededJs, ct).ConfigureAwait(false);
    }

    // Walks the element's clipping ancestors, crossing shadow roots, and then each frame it sits in
    // for as long as the frame's host element can be reached from inside it. A frame from another
    // origin hides its host, so the walk stops there and the caller has to look further itself.
    // Scrolling is instant whatever the page's scroll-behavior says, so the stability check that
    // follows measures the element where it settled rather than partway through an animation.
    internal const string ScrollIntoViewIfNeededJs = """
        function() {
            if (!this.isConnected) return false;
            function parentOf(node) {
                if (node.assignedSlot) return node.assignedSlot;
                if (node.parentElement) return node.parentElement;
                var root = node.parentNode;
                return root && root.host ? root.host : null;
            }
            function within(lo, hi, clipLo, clipHi) {
                if (hi - lo <= clipHi - clipLo) return lo >= clipLo && hi <= clipHi;
                var mid = (lo + hi) / 2;
                return mid >= clipLo && mid <= clipHi;
            }
            function shown(box, clip) {
                return within(box.left, box.right, clip.left, clip.right) &&
                       within(box.top, box.bottom, clip.top, clip.bottom);
            }
            function inView(el) {
                var r = el.getBoundingClientRect();
                var box = { left: r.left, top: r.top, right: r.right, bottom: r.bottom };
                var node = el;
                var win = el.ownerDocument.defaultView;
                while (win) {
                    var doc = win.document;
                    var root = doc.documentElement;
                    var rootStyle = win.getComputedStyle(root);
                    var bodyIsViewport = rootStyle.overflowX === 'visible' && rootStyle.overflowY === 'visible';
                    for (var p = parentOf(node); p; p = parentOf(p)) {
                        if (p === root || (p === doc.body && bodyIsViewport)) continue;
                        var s = win.getComputedStyle(p);
                        var clipX = s.overflowX !== 'visible';
                        var clipY = s.overflowY !== 'visible';
                        if (!clipX && !clipY) continue;
                        var pr = p.getBoundingClientRect();
                        var left = pr.left + p.clientLeft;
                        var top = pr.top + p.clientTop;
                        var clip = {
                            left: clipX ? left : -Infinity, right: clipX ? left + p.clientWidth : Infinity,
                            top: clipY ? top : -Infinity, bottom: clipY ? top + p.clientHeight : Infinity
                        };
                        if (!shown(box, clip)) return false;
                    }
                    var vw = win.innerWidth, vh = win.innerHeight;
                    if (doc.scrollingElement === root && root.clientWidth > 0 && root.clientHeight > 0) {
                        vw = Math.min(vw, root.clientWidth);
                        vh = Math.min(vh, root.clientHeight);
                    }
                    if (!shown(box, { left: 0, top: 0, right: vw, bottom: vh })) return false;
                    var owner = null;
                    try { owner = win.frameElement; } catch (e) { owner = null; }
                    if (!owner) return true;
                    var or = owner.getBoundingClientRect();
                    var os = owner.ownerDocument.defaultView.getComputedStyle(owner);
                    var dx = or.left + owner.clientLeft + (parseFloat(os.paddingLeft) || 0);
                    var dy = or.top + owner.clientTop + (parseFloat(os.paddingTop) || 0);
                    box = { left: box.left + dx, top: box.top + dy, right: box.right + dx, bottom: box.bottom + dy };
                    node = owner;
                    win = owner.ownerDocument.defaultView;
                }
                return true;
            }
            if (inView(this)) return false;
            this.scrollIntoView({ block: 'center', inline: 'center', behavior: 'instant' });
            return true;
        }
        """;

    private readonly record struct StabilityResult(bool IsStable, double Drift);

    private static async Task<StabilityResult> MeasureStabilityAsync(IMotusSession session, string objectId, CancellationToken ct)
    {
        // Use two measurements separated by a short delay instead of requestAnimationFrame.
        // In headless Chromium, requestAnimationFrame may stall on data: URIs or after DOM
        // mutations when the browser stops scheduling animation frames, causing a 30s hang.
        // Returns the total drift in px across x, y, width, and height.
        var result = await session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: """
                    function() {
                        var r1 = this.getBoundingClientRect();
                        var self = this;
                        return new Promise(function(resolve) {
                            setTimeout(function() {
                                var r2 = self.getBoundingClientRect();
                                var d = Math.abs(r1.x-r2.x) + Math.abs(r1.y-r2.y) +
                                        Math.abs(r1.width-r2.width) + Math.abs(r1.height-r2.height);
                                resolve(d);
                            }, 50);
                        });
                    }
                    """,
                ObjectId: objectId,
                ReturnByValue: true,
                AwaitPromise: true),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Actionability check failed: {result.ExceptionDetails.Text}");

        var drift = result.Result.Value is JsonElement el && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : 0;

        return new StabilityResult(drift < 2, drift);
    }

    private static async Task<bool> ReceivesEventsAsync(IMotusSession session, string objectId, CancellationToken ct)
    {
        // Pure JS hit-test using document.elementFromPoint at the element's center.
        // Checks multiple conditions for whether the click will reach our target:
        // 1. The topmost element IS our target or a child of it (standard case)
        // 2. The covering element has pointer-events:none (clicks pass through)
        // 3. The covering element is a descendant of our target's parent that would
        //    bubble the event up to a shared interactive ancestor
        return await EvalBoolAsync(session, objectId,
            """
            function() {
                var r = this.getBoundingClientRect();
                var cx = r.x + r.width / 2;
                var cy = r.y + r.height / 2;
                var top = document.elementFromPoint(cx, cy);
                if (top === null) return false;
                // Direct hit or child of target
                if (this === top || this.contains(top)) return true;
                // Covering element has pointer-events:none (transparent to clicks)
                var style = window.getComputedStyle(top);
                if (style.pointerEvents === 'none') return true;
                // Walk up from the covering element; if our target is an ancestor,
                // the event will bubble up to it
                var el = top;
                while (el) {
                    if (el === this) return true;
                    el = el.parentElement;
                }
                // Check if the covering element and target share a common parent.
                // In complex widgets, a label sibling may cover the target but the
                // click bubbles to the shared parent where the real event handler lives.
                var targetParent = this.parentElement;
                if (targetParent) {
                    el = top;
                    while (el) {
                        if (el === targetParent) return true;
                        el = el.parentElement;
                    }
                }
                return false;
            }
            """, ct).ConfigureAwait(false);
    }

    private static async Task<string> GetCoveringElementInfoAsync(IMotusSession session, string objectId, CancellationToken ct)
    {
        try
        {
            var result = await session.SendAsync(
                "Runtime.callFunctionOn",
                new RuntimeCallFunctionOnParams(
                    FunctionDeclaration: """
                        function() {
                            var r = this.getBoundingClientRect();
                            var cx = r.x + r.width / 2;
                            var cy = r.y + r.height / 2;
                            var top = document.elementFromPoint(cx, cy);
                            if (!top) return 'unknown';
                            var tag = top.tagName.toLowerCase();
                            if (top.id) tag += '#' + top.id;
                            else if (top.className && typeof top.className === 'string') {
                                var cls = top.className.trim().split(/\s+/).slice(0, 2).join('.');
                                if (cls) tag += '.' + cls;
                            }
                            return tag;
                        }
                        """,
                    ObjectId: objectId,
                    ReturnByValue: true,
                    AwaitPromise: false),
                CdpJsonContext.Default.RuntimeCallFunctionOnParams,
                CdpJsonContext.Default.RuntimeCallFunctionOnResult,
                ct).ConfigureAwait(false);

            if (result.Result.Value is JsonElement el && el.ValueKind == JsonValueKind.String)
                return el.GetString() ?? "unknown";
        }
        catch
        {
            // Best-effort diagnostic
        }

        return "unknown";
    }

    private static async Task<bool> EvalBoolAsync(
        IMotusSession session, string objectId, string jsFunction, CancellationToken ct,
        bool awaitPromise = false)
    {
        var result = await session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: jsFunction,
                ObjectId: objectId,
                ReturnByValue: true,
                AwaitPromise: awaitPromise),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Actionability check failed: {result.ExceptionDetails.Text}");

        if (result.Result.Value is JsonElement element && element.ValueKind == JsonValueKind.True)
            return true;

        return false;
    }
}
