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

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                lastCheckName = "resolve";

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
                    if (flags.HasFlag(ActionabilityFlags.Visible) && !await IsVisibleAsync(page, objectId, ct).ConfigureAwait(false))
                    {
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "enabled";
                    if (flags.HasFlag(ActionabilityFlags.Enabled) && !await IsEnabledAsync(page, objectId, ct).ConfigureAwait(false))
                    {
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "editable";
                    if (flags.HasFlag(ActionabilityFlags.Editable) && !await IsEditableAsync(page, objectId, ct).ConfigureAwait(false))
                    {
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "stable";
                    if (flags.HasFlag(ActionabilityFlags.Stable) && !await IsStableAsync(page, objectId, ct).ConfigureAwait(false))
                    {
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
                    }

                    lastCheckName = "receivesEvents";
                    if (flags.HasFlag(ActionabilityFlags.ReceivesEvents) && !await ReceivesEventsAsync(page, objectId, ct).ConfigureAwait(false))
                    {
                        await Task.Delay(PollingIntervalMs, ct).ConfigureAwait(false);
                        continue;
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
            throw new ActionTimeoutException(
                selector, lastCheckName, elementState: null, pageUrl: page.Url,
                timeoutDuration: TimeSpan.FromMilliseconds(DefaultTimeoutMs),
                message: $"Actionability check '{lastCheckName}' timed out for selector '{selector}'.");
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

    private static async Task<bool> IsStableAsync(Page page, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(page, objectId,
            """
            function() {
                return new Promise(resolve => {
                    var r1 = this.getBoundingClientRect();
                    requestAnimationFrame(() => {
                        var r2 = this.getBoundingClientRect();
                        resolve(
                            r1.x === r2.x && r1.y === r2.y &&
                            r1.width === r2.width && r1.height === r2.height
                        );
                    });
                });
            }
            """, ct, awaitPromise: true).ConfigureAwait(false);
    }

    private static async Task<bool> ReceivesEventsAsync(Page page, string objectId, CancellationToken ct)
    {
        // Pure JS hit-test using document.elementFromPoint at the element's center.
        // This avoids the CDP DOM domain (DOM.getNodeForLocation + DOM.resolveNode)
        // which fails for elements with text children because DOM.resolveNode returns
        // a null objectId for text nodes, causing the check to always return false.
        // elementFromPoint returns the nearest Element (not text node), making
        // this.contains(top) work correctly for buttons, links, and other text-bearing elements.
        return await EvalBoolAsync(page, objectId,
            """
            function() {
                var r = this.getBoundingClientRect();
                var cx = r.x + r.width / 2;
                var cy = r.y + r.height / 2;
                var top = document.elementFromPoint(cx, cy);
                return top !== null && (this === top || this.contains(top));
            }
            """, ct).ConfigureAwait(false);
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
