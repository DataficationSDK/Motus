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
                    objectId = await resolveObjectId(ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is InvalidOperationException or MotusSelectorException)
                {
                    await Task.Delay(PollingIntervalMs, ct);
                    continue;
                }

                try
                {
                    if (flags == ActionabilityFlags.None)
                        return objectId;

                    lastCheckName = "visible";
                    if (flags.HasFlag(ActionabilityFlags.Visible) && !await IsVisibleAsync(page, objectId, ct))
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                        continue;
                    }

                    lastCheckName = "enabled";
                    if (flags.HasFlag(ActionabilityFlags.Enabled) && !await IsEnabledAsync(page, objectId, ct))
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                        continue;
                    }

                    lastCheckName = "editable";
                    if (flags.HasFlag(ActionabilityFlags.Editable) && !await IsEditableAsync(page, objectId, ct))
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                        continue;
                    }

                    lastCheckName = "stable";
                    if (flags.HasFlag(ActionabilityFlags.Stable) && !await IsStableAsync(page, objectId, ct))
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                        continue;
                    }

                    lastCheckName = "receivesEvents";
                    if (flags.HasFlag(ActionabilityFlags.ReceivesEvents) && !await ReceivesEventsAsync(page, objectId, ct))
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                        continue;
                    }

                    return objectId;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is InvalidOperationException or MotusSelectorException)
                {
                    // Element went stale during checks, retry from scratch
                    await Task.Delay(PollingIntervalMs, ct);
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
            """, ct);
    }

    private static async Task<bool> IsEnabledAsync(Page page, string objectId, CancellationToken ct)
    {
        return await EvalBoolAsync(page, objectId,
            "function() { return !this.disabled && this.getAttribute('aria-disabled') !== 'true'; }",
            ct);
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
            """, ct);
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
            """, ct, awaitPromise: true);
    }

    private static async Task<bool> ReceivesEventsAsync(Page page, string objectId, CancellationToken ct)
    {
        // Get bounding box center
        var boxResult = await page.Session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: """
                    function() {
                        var r = this.getBoundingClientRect();
                        return { x: r.x, y: r.y, width: r.width, height: r.height };
                    }
                    """,
                ObjectId: objectId,
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct);

        if (boxResult.Result.Value is not JsonElement boxEl)
            return false;

        var box = boxEl.Deserialize<BoundingBox>();
        if (box is null)
            return false;

        var cx = (int)(box.X + box.Width / 2);
        var cy = (int)(box.Y + box.Height / 2);

        // Get the topmost node at the center point
        var nodeResult = await page.Session.SendAsync(
            "DOM.getNodeForLocation",
            new DomGetNodeForLocationParams(cx, cy),
            CdpJsonContext.Default.DomGetNodeForLocationParams,
            CdpJsonContext.Default.DomGetNodeForLocationResult,
            ct);

        // Resolve the hit-tested node to a remote object
        var resolvedResult = await page.Session.SendAsync(
            "DOM.resolveNode",
            new DomResolveNodeParams(BackendNodeId: nodeResult.BackendNodeId, ObjectGroup: "motus-actionability"),
            CdpJsonContext.Default.DomResolveNodeParams,
            CdpJsonContext.Default.DomResolveNodeResult,
            ct);

        if (resolvedResult.Object.ObjectId is null)
            return false;

        // Check if the original element is or contains the hit-tested element
        var identityResult = await page.Session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: "function(top) { return this === top || this.contains(top); }",
                ObjectId: objectId,
                Arguments: [new RuntimeCallArgument(ObjectId: resolvedResult.Object.ObjectId)],
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct);

        if (identityResult.Result.Value is JsonElement val && val.ValueKind == JsonValueKind.True)
            return true;

        return false;
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
            ct);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Actionability check failed: {result.ExceptionDetails.Text}");

        if (result.Result.Value is JsonElement element && element.ValueKind == JsonValueKind.True)
            return true;

        return false;
    }
}
