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
}

internal static class ActionabilityChecker
{
    internal const int PollingIntervalMs = 100;
    internal const double DefaultTimeoutMs = 30_000;

    internal static async Task<string> WaitForActionabilityAsync(
        Page page,
        Func<CancellationToken, Task<string>> resolveObjectId,
        ActionabilityFlags flags,
        string selector,
        CancellationToken ct)
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
                    if (flags.HasFlag(ActionabilityFlags.Visible) && !await IsVisibleAsync(page, objectId, ct).ConfigureAwait(false))
                    {
                        lastCheckDetail = "element was found but is not visible (display:none, visibility:hidden, zero size, or opacity:0)";
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "enabled";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.Enabled) && !await IsEnabledAsync(page, objectId, ct).ConfigureAwait(false))
                    {
                        lastCheckDetail = "element was found but is disabled";
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "editable";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.Editable) && !await IsEditableAsync(page, objectId, ct).ConfigureAwait(false))
                    {
                        lastCheckDetail = "element was found but is not editable";
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "stable";
                    lastCheckDetail = null;
                    if (flags.HasFlag(ActionabilityFlags.Stable))
                    {
                        var stableResult = await MeasureStabilityAsync(page, objectId, ct).ConfigureAwait(false);
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
                        var hitsTarget = await ReceivesEventsAsync(page, objectId, ct).ConfigureAwait(false);
                        if (!hitsTarget)
                        {
                            var coveringTag = await GetCoveringElementInfoAsync(page, objectId, ct).ConfigureAwait(false);
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

    private static async Task<bool> IsVisibleAsync(Page page, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(page, objectId,
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

    private static async Task<bool> IsEnabledAsync(Page page, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(page, objectId,
            "function() { return !this.disabled && this.getAttribute('aria-disabled') !== 'true'; }",
            ct).ConfigureAwait(false);
    }

    private static async Task<bool> IsEditableAsync(Page page, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(page, objectId,
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

    private readonly record struct StabilityResult(bool IsStable, double Drift);

    private static async Task<StabilityResult> MeasureStabilityAsync(Page page, string objectId, CancellationToken ct)
    {
        // Use two measurements separated by a short delay instead of requestAnimationFrame.
        // In headless Chromium, requestAnimationFrame may stall on data: URIs or after DOM
        // mutations when the browser stops scheduling animation frames, causing a 30s hang.
        // Returns the total drift in px across x, y, width, and height.
        var result = await page.Session.SendAsync(
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

    private static async Task<bool> ReceivesEventsAsync(Page page, string objectId, CancellationToken ct)
    {
        // Pure JS hit-test using document.elementFromPoint at the element's center.
        // Checks multiple conditions for whether the click will reach our target:
        // 1. The topmost element IS our target or a child of it (standard case)
        // 2. The covering element has pointer-events:none (clicks pass through)
        // 3. The covering element is a descendant of our target's parent that would
        //    bubble the event up to a shared interactive ancestor
        return await EvalBoolAsync(page, objectId,
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

    private static async Task<string> GetCoveringElementInfoAsync(Page page, string objectId, CancellationToken ct)
    {
        try
        {
            var result = await page.Session.SendAsync(
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
        Page page, string objectId, string jsFunction, CancellationToken ct,
        bool awaitPromise = false)
    {
        var result = await page.Session.SendAsync(
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
