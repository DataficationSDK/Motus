using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

internal sealed class Locator : ILocator
{
    private readonly Page _page;
    private readonly string _selector;
    private readonly int? _nthIndex;  // null=strict single, 0=first, -1=last, n=nth
    private readonly string? _hasText;
    private readonly ILocator? _has;
    private readonly ILocator? _hasNot;
    private readonly double? _defaultTimeout;
    private readonly bool _pierceShadow;

    internal Locator(Page page, string selector, LocatorOptions? options = null)
    {
        _page = page;
        _selector = selector;
        _hasText = options?.HasText;
        _has = options?.Has;
        _hasNot = options?.HasNot;
        _defaultTimeout = options?.Timeout;
        _pierceShadow = options?.PierceShadow ?? true;
    }

    private Locator(Page page, string selector, int? nthIndex, string? hasText,
        ILocator? has, ILocator? hasNot, double? defaultTimeout = null, bool pierceShadow = true)
    {
        _page = page;
        _selector = selector;
        _nthIndex = nthIndex;
        _hasText = hasText;
        _has = has;
        _hasNot = hasNot;
        _defaultTimeout = defaultTimeout;
        _pierceShadow = pierceShadow;
    }

    // --- Internal Properties for Assertions ---

    internal string Selector => _selector;
    internal string PageUrl => _page.Url;

    internal async Task<bool> IsEmptyAsync(CancellationToken ct)
    {
        var objectId = await ResolveObjectIdCoreAsync(ct).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId,
            """
            function() {
                var tag = this.tagName.toLowerCase();
                if (tag === 'input' || tag === 'textarea' || tag === 'select')
                    return this.value === '';
                return this.textContent.trim() === '';
            }
            """, null, ct).ConfigureAwait(false);
    }

    internal async Task<string?> GetComputedStyleAsync(string property, CancellationToken ct)
    {
        var objectId = await ResolveObjectIdCoreAsync(ct).ConfigureAwait(false);
        return await EvalOnElementAsync<string?>(objectId,
            "function(prop) { return window.getComputedStyle(this).getPropertyValue(prop); }",
            [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(property))], ct).ConfigureAwait(false);
    }

    internal async Task<bool> HasClassAsync(string className, CancellationToken ct)
    {
        var objectId = await ResolveObjectIdCoreAsync(ct).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId,
            "function(cls) { return this.classList.contains(cls); }",
            [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(className))], ct).ConfigureAwait(false);
    }

    // --- Accessibility Queries for Assertions ---

    private volatile bool _accessibilityEnabled;

    internal async Task<string?> GetAccessibilityNameAsync(CancellationToken ct)
    {
        var node = await QuerySingleAXNodeAsync(ct).ConfigureAwait(false);
        return node?.Name?.Value?.GetString();
    }

    internal async Task<string?> GetAccessibilityRoleAsync(CancellationToken ct)
    {
        var node = await QuerySingleAXNodeAsync(ct).ConfigureAwait(false);
        return node?.Role?.Value?.GetString();
    }

    private async Task<AccessibilityAXNodeSimple?> QuerySingleAXNodeAsync(CancellationToken ct)
    {
        var objectId = await ResolveObjectIdCoreAsync(ct).ConfigureAwait(false);

        if (!_accessibilityEnabled)
        {
            await _page.Session.SendAsync(
                "Accessibility.enable",
                CdpJsonContext.Default.AccessibilityEnableResult,
                ct).ConfigureAwait(false);
            _accessibilityEnabled = true;
        }

        var result = await _page.Session.SendAsync(
            "Accessibility.queryAXTree",
            new AccessibilityQueryAXTreeParams(
                ObjectId: objectId,
                AccessibleName: null,
                Role: null),
            CdpJsonContext.Default.AccessibilityQueryAXTreeParams,
            CdpJsonContext.Default.AccessibilityQueryAXTreeResult,
            ct).ConfigureAwait(false);

        return result.Nodes.FirstOrDefault(n => !n.Ignored);
    }

    // --- Timeout Helper ---

    private CancellationTokenSource BuildActionCts(double? methodTimeout)
    {
        var ms = methodTimeout ?? _defaultTimeout ?? ActionabilityChecker.DefaultTimeoutMs;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_page.PageLifetimeToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(ms));
        return cts;
    }

    // --- Prefix Parser ---

    internal static (string prefix, string expression) ParsePrefix(string selector)
    {
        var span = selector.AsSpan();
        var eqIdx = span.IndexOf('=');

        if (eqIdx < 0)
            return ("css", selector);

        var candidate = span[..eqIdx];

        // If the candidate prefix contains CSS structural chars, treat entire string as CSS
        foreach (var ch in candidate)
        {
            if (ch is ' ' or '[' or ']' or '.' or '#' or '>' or '~' or '+' or ':' or ',')
                return ("css", selector);
        }

        return (candidate.ToString(), span[(eqIdx + 1)..].ToString());
    }

    // --- Core Resolution ---

    private async Task<string> ResolveObjectIdCoreAsync(CancellationToken ct)
    {
        var (prefix, expression) = ParsePrefix(_selector);
        var registry = _page.ContextInternal.SelectorStrategies;

        if (!registry.TryGetStrategy(prefix, out var strategy))
            throw new InvalidOperationException($"No selector strategy registered for prefix: {prefix}");

        var handles = await strategy!.ResolveAsync(expression, _page.GetFrameForSelectors(), _pierceShadow, ct).ConfigureAwait(false);

        // Apply hasText filter
        if (_hasText is not null)
        {
            var filtered = new List<IElementHandle>();
            foreach (var handle in handles)
            {
                var text = await handle.TextContentAsync(ct).ConfigureAwait(false);
                if (text is not null && text.Contains(_hasText, StringComparison.Ordinal))
                    filtered.Add(handle);
            }
            handles = filtered;
        }

        // Apply nth index selection
        IElementHandle? selected;
        if (_nthIndex is null)
        {
            if (handles.Count == 0)
                throw new ElementNotFoundException(_selector, _page.Url);
            selected = handles[0];
        }
        else if (_nthIndex == -1)
        {
            if (handles.Count == 0)
                throw new ElementNotFoundException(_selector, _page.Url);
            selected = handles[^1];
        }
        else
        {
            var idx = _nthIndex.Value;
            if (idx < 0 || idx >= handles.Count)
                throw new ElementNotFoundException(_selector, _page.Url);
            selected = handles[idx];
        }

        return ((ElementHandle)selected).ObjectId;
    }

    private async Task<T> EvalOnElementAsync<T>(string objectId, string jsFunction,
        RuntimeCallArgument[]? args, CancellationToken ct)
    {
        var result = await _page.Session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: jsFunction,
                ObjectId: objectId,
                Arguments: args,
                ReturnByValue: true,
                AwaitPromise: true),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Evaluation failed: {result.ExceptionDetails.Text}");

        if (result.Result.Value is JsonElement element)
            return element.Deserialize<T>(CdpJsonContext.Default.Options)!;

        if (result.Result.Type == "undefined" ||
            (result.Result.Type == "object" && result.Result.Subtype == "null"))
            return default!;

        throw new InvalidOperationException(
            $"Cannot deserialize result of type '{result.Result.Type}'.");
    }

    private async Task EvalOnElementVoidAsync(string objectId, string jsFunction,
        RuntimeCallArgument[]? args, CancellationToken ct)
    {
        var result = await _page.Session.SendAsync(
            "Runtime.callFunctionOn",
            new RuntimeCallFunctionOnParams(
                FunctionDeclaration: jsFunction,
                ObjectId: objectId,
                Arguments: args,
                ReturnByValue: false,
                AwaitPromise: true),
            CdpJsonContext.Default.RuntimeCallFunctionOnParams,
            CdpJsonContext.Default.RuntimeCallFunctionOnResult,
            ct).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Evaluation failed: {result.ExceptionDetails.Text}");
    }

    private async Task<BoundingBox> GetBoundingBoxOrThrowAsync(string objectId, CancellationToken ct)
    {
        var box = await EvalOnElementAsync<BoundingBox?>(objectId,
            """
            function() {
                var r = this.getBoundingClientRect();
                if (r.width === 0 && r.height === 0) return null;
                return { x: r.x, y: r.y, width: r.width, height: r.height };
            }
            """, null, ct).ConfigureAwait(false);

        return box ?? throw new InvalidOperationException("Element has zero size bounding box.");
    }

    // --- Action Hook Helper ---

    private async Task RunWithHooksAsync(string actionName, Func<Task> action)
    {
        ActionContext.CurrentSelector.Value = _selector;
        try
        {
            await _page.ContextInternal.LifecycleHooks.FireBeforeActionAsync(_page, actionName).ConfigureAwait(false);
            Exception? error = null;
            try
            {
                await action().ConfigureAwait(false);
            }
            catch (MotusException mex) when (mex.Screenshot is null)
            {
                error = mex;
                await FailureCapture.AttachScreenshotAsync(mex, _page).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                await _page.ContextInternal.LifecycleHooks.FireAfterActionAsync(
                    _page, actionName, new ActionResult(actionName, error)).ConfigureAwait(false);
            }
        }
        finally
        {
            ActionContext.CurrentSelector.Value = null;
        }
    }

    private async Task<T> RunWithHooksAsync<T>(string actionName, Func<Task<T>> action)
    {
        ActionContext.CurrentSelector.Value = _selector;
        try
        {
            await _page.ContextInternal.LifecycleHooks.FireBeforeActionAsync(_page, actionName).ConfigureAwait(false);
            Exception? error = null;
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (MotusException mex) when (mex.Screenshot is null)
            {
                error = mex;
                await FailureCapture.AttachScreenshotAsync(mex, _page).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                await _page.ContextInternal.LifecycleHooks.FireAfterActionAsync(
                    _page, actionName, new ActionResult(actionName, error)).ConfigureAwait(false);
            }
        }
        finally
        {
            ActionContext.CurrentSelector.Value = null;
        }
    }

    // --- Chaining Properties ---

    public ILocator First => new Locator(_page, _selector, 0, _hasText, _has, _hasNot, _defaultTimeout, _pierceShadow);

    public ILocator Last => new Locator(_page, _selector, -1, _hasText, _has, _hasNot, _defaultTimeout, _pierceShadow);

    public ILocator Nth(int index) => new Locator(_page, _selector, index, _hasText, _has, _hasNot, _defaultTimeout, _pierceShadow);

    public ILocator Filter(LocatorOptions? options = null) =>
        new Locator(_page, _selector, _nthIndex,
            options?.HasText ?? _hasText,
            options?.Has ?? _has,
            options?.HasNot ?? _hasNot,
            _defaultTimeout,
            options?.PierceShadow ?? _pierceShadow);

    ILocator ILocator.Locator(string selector, LocatorOptions? options = null) =>
        new Locator(_page, _selector + " " + selector, options is null
            ? new LocatorOptions { PierceShadow = _pierceShadow }
            : options with { PierceShadow = options.PierceShadow ?? _pierceShadow });

    // --- Action Methods ---

    public async Task ClickAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("click", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled | ActionabilityFlags.Stable | ActionabilityFlags.ReceivesEvents,
                _selector, cts.Token).ConfigureAwait(false);
            var box = await GetBoundingBoxOrThrowAsync(objectId, cts.Token).ConfigureAwait(false);
            await _page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task DblClickAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("dblclick", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled | ActionabilityFlags.Stable | ActionabilityFlags.ReceivesEvents,
                _selector, cts.Token).ConfigureAwait(false);
            var box = await GetBoundingBoxOrThrowAsync(objectId, cts.Token).ConfigureAwait(false);
            await _page.Mouse.DblClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task FillAsync(string value, double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("fill", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled | ActionabilityFlags.Editable,
                _selector, cts.Token).ConfigureAwait(false);
            await EvalOnElementVoidAsync(objectId,
                """
                function(value) {
                    this.focus();
                    this.value = value;
                    this.dispatchEvent(new Event('input', { bubbles: true }));
                    this.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """,
                [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(value))],
                cts.Token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task ClearAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("clear", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled | ActionabilityFlags.Editable,
                _selector, cts.Token).ConfigureAwait(false);
            await EvalOnElementVoidAsync(objectId,
                """
                function() {
                    this.focus();
                    this.value = '';
                    this.dispatchEvent(new Event('input', { bubbles: true }));
                    this.dispatchEvent(new Event('change', { bubbles: true }));
                }
                """, null, cts.Token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task TypeAsync(string text, KeyboardTypeOptions? options = null)
    {
        using var cts = BuildActionCts(null);
        await RunWithHooksAsync("type", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled | ActionabilityFlags.Editable,
                _selector, cts.Token).ConfigureAwait(false);
            await EvalOnElementVoidAsync(objectId, "function() { this.focus(); }", null, cts.Token).ConfigureAwait(false);
            await _page.Keyboard.TypeAsync(text, options).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task PressAsync(string key, KeyboardPressOptions? options = null)
    {
        using var cts = BuildActionCts(null);
        await RunWithHooksAsync("press", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled | ActionabilityFlags.Editable,
                _selector, cts.Token).ConfigureAwait(false);
            await EvalOnElementVoidAsync(objectId, "function() { this.focus(); }", null, cts.Token).ConfigureAwait(false);
            await _page.Keyboard.PressAsync(key, options).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task CheckAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("check", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled,
                _selector, cts.Token).ConfigureAwait(false);
            var isChecked = await EvalOnElementAsync<bool>(objectId, "function() { return this.checked; }", null, cts.Token).ConfigureAwait(false);
            if (!isChecked)
            {
                var box = await GetBoundingBoxOrThrowAsync(objectId, cts.Token).ConfigureAwait(false);
                await _page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public async Task UncheckAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("uncheck", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled,
                _selector, cts.Token).ConfigureAwait(false);
            var isChecked = await EvalOnElementAsync<bool>(objectId, "function() { return this.checked; }", null, cts.Token).ConfigureAwait(false);
            if (isChecked)
            {
                var box = await GetBoundingBoxOrThrowAsync(objectId, cts.Token).ConfigureAwait(false);
                await _page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    public async Task SetCheckedAsync(bool @checked, double? timeout = null)
    {
        if (@checked)
            await CheckAsync(timeout).ConfigureAwait(false);
        else
            await UncheckAsync(timeout).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> SelectOptionAsync(params string[] values)
    {
        using var cts = BuildActionCts(null);
        return await RunWithHooksAsync("selectOption", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled,
                _selector, cts.Token).ConfigureAwait(false);
            return await EvalOnElementAsync<string[]>(objectId,
                """
                function(values) {
                    const options = Array.from(this.options);
                    const selected = [];
                    for (const opt of options) {
                        opt.selected = values.includes(opt.value);
                        if (opt.selected) selected.push(opt.value);
                    }
                    this.dispatchEvent(new Event('input', { bubbles: true }));
                    this.dispatchEvent(new Event('change', { bubbles: true }));
                    return selected;
                }
                """,
                [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(values))],
                cts.Token).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task SetInputFilesAsync(IEnumerable<FilePayload> files, double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("setInputFiles", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled,
                _selector, cts.Token).ConfigureAwait(false);
            var tempFiles = new List<string>();

            try
            {
                foreach (var file in files)
                {
                    var tempPath = Path.Combine(Path.GetTempPath(), file.Name);
                    await File.WriteAllBytesAsync(tempPath, file.Buffer).ConfigureAwait(false);
                    tempFiles.Add(tempPath);
                }

                await _page.Session.SendAsync(
                    "DOM.setFileInputFiles",
                    new DomSetFileInputFilesParams(
                        Files: tempFiles.ToArray(),
                        ObjectId: objectId),
                    CdpJsonContext.Default.DomSetFileInputFilesParams,
                    CdpJsonContext.Default.DomSetFileInputFilesResult,
                    cts.Token).ConfigureAwait(false);
            }
            finally
            {
                foreach (var tempFile in tempFiles)
                {
                    try { File.Delete(tempFile); } catch { /* best effort */ }
                }
            }
        }).ConfigureAwait(false);
    }

    public async Task HoverAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("hover", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Stable | ActionabilityFlags.ReceivesEvents,
                _selector, cts.Token).ConfigureAwait(false);
            var box = await GetBoundingBoxOrThrowAsync(objectId, cts.Token).ConfigureAwait(false);
            await _page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task FocusAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
            _page, ResolveObjectIdCoreAsync,
            ActionabilityFlags.None,
            _selector, cts.Token).ConfigureAwait(false);
        await EvalOnElementVoidAsync(objectId, "function() { this.focus(); }", null, cts.Token).ConfigureAwait(false);
    }

    public async Task TapAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        await RunWithHooksAsync("tap", async () =>
        {
            var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
                _page, ResolveObjectIdCoreAsync,
                ActionabilityFlags.Visible | ActionabilityFlags.Enabled | ActionabilityFlags.Stable | ActionabilityFlags.ReceivesEvents,
                _selector, cts.Token).ConfigureAwait(false);
            var box = await GetBoundingBoxOrThrowAsync(objectId, cts.Token).ConfigureAwait(false);
            await _page.Touchscreen.TapAsync(box.X + box.Width / 2, box.Y + box.Height / 2).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    public async Task ScrollIntoViewIfNeededAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
            _page, ResolveObjectIdCoreAsync,
            ActionabilityFlags.None,
            _selector, cts.Token).ConfigureAwait(false);
        await EvalOnElementVoidAsync(objectId,
            "function() { this.scrollIntoViewIfNeeded ? this.scrollIntoViewIfNeeded() : this.scrollIntoView({ block: 'center' }); }",
            null, cts.Token).ConfigureAwait(false);
    }

    public async Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null)
    {
        using var cts = BuildActionCts(null);
        var objectId = await ActionabilityChecker.WaitForActionabilityAsync(
            _page, ResolveObjectIdCoreAsync,
            ActionabilityFlags.Visible,
            _selector, cts.Token).ConfigureAwait(false);
        var box = await GetBoundingBoxOrThrowAsync(objectId, cts.Token).ConfigureAwait(false);

        var format = options?.Type == ScreenshotType.Jpeg ? "jpeg" : "png";
        var quality = options?.Type == ScreenshotType.Jpeg ? options.Quality : null;

        var result = await _page.Session.SendAsync(
            "Page.captureScreenshot",
            new PageCaptureScreenshotWithClipParams(
                Clip: new PageClipRect(box.X, box.Y, box.Width, box.Height),
                Format: format,
                Quality: quality),
            CdpJsonContext.Default.PageCaptureScreenshotWithClipParams,
            CdpJsonContext.Default.PageCaptureScreenshotResult,
            cts.Token).ConfigureAwait(false);

        var bytes = Convert.FromBase64String(result.Data);

        if (options?.Path is not null)
        {
            var dir = Path.GetDirectoryName(options.Path);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(options.Path, bytes).ConfigureAwait(false);
        }

        return bytes;
    }

    public async Task DispatchEventAsync(string type, object? eventInit = null)
    {
        using var cts = BuildActionCts(null);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        await EvalOnElementVoidAsync(objectId,
            """
            function(type, eventInit) {
                const event = new Event(type, eventInit || { bubbles: true });
                this.dispatchEvent(event);
            }
            """,
            [
                new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(type)),
                new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(eventInit))
            ], cts.Token).ConfigureAwait(false);
    }

    public async Task<T> EvaluateAsync<T>(string expression, object? arg = null)
    {
        using var cts = BuildActionCts(null);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        var args = arg is not null
            ? new[] { new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(arg)) }
            : (RuntimeCallArgument[]?)null;
        return await EvalOnElementAsync<T>(objectId, expression, args, cts.Token).ConfigureAwait(false);
    }

    // --- Query Methods ---

    public async Task<string?> TextContentAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<string?>(objectId, "function() { return this.textContent; }", null, cts.Token).ConfigureAwait(false);
    }

    public async Task<string> InnerTextAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<string>(objectId, "function() { return this.innerText; }", null, cts.Token).ConfigureAwait(false);
    }

    public async Task<string> InnerHTMLAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<string>(objectId, "function() { return this.innerHTML; }", null, cts.Token).ConfigureAwait(false);
    }

    public async Task<string?> GetAttributeAsync(string name, double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<string?>(objectId,
            "function(name) { return this.getAttribute(name); }",
            [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(name))], cts.Token).ConfigureAwait(false);
    }

    public async Task<string> InputValueAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<string>(objectId, "function() { return this.value; }", null, cts.Token).ConfigureAwait(false);
    }

    public async Task<BoundingBox?> BoundingBoxAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<BoundingBox?>(objectId,
            """
            function() {
                var r = this.getBoundingClientRect();
                if (r.width === 0 && r.height === 0) return null;
                return { x: r.x, y: r.y, width: r.width, height: r.height };
            }
            """, null, cts.Token).ConfigureAwait(false);
    }

    public async Task<int> CountAsync()
    {
        using var cts = BuildActionCts(null);
        var handles = await ResolveAllHandlesAsync(cts.Token).ConfigureAwait(false);
        return handles.Count;
    }

    public async Task<IReadOnlyList<string>> AllInnerTextsAsync()
    {
        using var cts = BuildActionCts(null);
        var handles = await ResolveAllHandlesAsync(cts.Token).ConfigureAwait(false);
        var results = new List<string>();
        foreach (var handle in handles)
        {
            var objectId = ((ElementHandle)handle).ObjectId;
            var text = await EvalOnElementAsync<string>(objectId,
                "function() { return this.innerText; }", null, cts.Token).ConfigureAwait(false);
            results.Add(text);
        }
        return results;
    }

    public async Task<IReadOnlyList<string>> AllTextContentsAsync()
    {
        using var cts = BuildActionCts(null);
        var handles = await ResolveAllHandlesAsync(cts.Token).ConfigureAwait(false);
        var results = new List<string>();
        foreach (var handle in handles)
        {
            var text = await handle.TextContentAsync(cts.Token).ConfigureAwait(false);
            results.Add(text ?? string.Empty);
        }
        return results;
    }

    // --- State Query Methods ---

    public async Task<bool> IsCheckedAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId, "function() { return !!this.checked; }", null, cts.Token).ConfigureAwait(false);
    }

    public async Task<bool> IsDisabledAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId, "function() { return !!this.disabled; }", null, cts.Token).ConfigureAwait(false);
    }

    public async Task<bool> IsEnabledAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId, "function() { return !this.disabled; }", null, cts.Token).ConfigureAwait(false);
    }

    public async Task<bool> IsEditableAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId,
            """
            function() {
                if (this.contentEditable === 'true') return true;
                var tag = this.tagName.toLowerCase();
                if (tag === 'input' || tag === 'textarea' || tag === 'select') return !this.disabled && !this.readOnly;
                return false;
            }
            """, null, cts.Token).ConfigureAwait(false);
    }

    public async Task<bool> IsVisibleAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId,
            """
            function() {
                var style = window.getComputedStyle(this);
                if (style.display === 'none') return false;
                if (style.visibility === 'hidden') return false;
                if (parseFloat(style.opacity) === 0) return false;
                var r = this.getBoundingClientRect();
                return r.width > 0 && r.height > 0;
            }
            """, null, cts.Token).ConfigureAwait(false);
    }

    public async Task<bool> IsHiddenAsync(double? timeout = null)
    {
        return !await IsVisibleAsync(timeout).ConfigureAwait(false);
    }

    // --- Waiting ---

    public async Task WaitForAsync(ElementState? state = null, double? timeout = null)
    {
        var target = state ?? ElementState.Visible;
        using var cts = BuildActionCts(timeout);
        var ct = cts.Token;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var satisfied = target switch
                {
                    ElementState.Attached => await TryResolveExistsAsync(ct).ConfigureAwait(false),
                    ElementState.Detached => !await TryResolveExistsAsync(ct).ConfigureAwait(false),
                    ElementState.Visible => await TryCheckVisibleAsync(ct).ConfigureAwait(false),
                    ElementState.Hidden => await TryCheckHiddenAsync(ct).ConfigureAwait(false),
                    _ => false,
                };
                if (satisfied) return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or MotusSelectorException
                && target is ElementState.Detached or ElementState.Hidden)
            {
                return; // element not found satisfies Detached/Hidden
            }
            catch (Exception ex) when (ex is InvalidOperationException or MotusSelectorException)
            { /* not found, retry for Attached/Visible */ }
            catch (OperationCanceledException)
            {
                throw new WaitTimeoutException(
                    condition: target.ToString(),
                    timeoutDuration: TimeSpan.FromMilliseconds(timeout ?? _defaultTimeout ?? ActionabilityChecker.DefaultTimeoutMs),
                    lastEvaluatedValue: null,
                    message: $"WaitForAsync('{target}') timed out for selector '{_selector}'.");
            }
            await Task.Delay(ActionabilityChecker.PollingIntervalMs, ct).ConfigureAwait(false);
        }
    }

    private async Task<bool> TryResolveExistsAsync(CancellationToken ct)
    {
        await ResolveObjectIdCoreAsync(ct).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> TryCheckVisibleAsync(CancellationToken ct)
    {
        var objectId = await ResolveObjectIdCoreAsync(ct).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId,
            """
            function() {
                var style = window.getComputedStyle(this);
                if (style.display === 'none') return false;
                if (style.visibility === 'hidden') return false;
                if (parseFloat(style.opacity) === 0) return false;
                var r = this.getBoundingClientRect();
                return r.width > 0 && r.height > 0;
            }
            """, null, ct).ConfigureAwait(false);
    }

    private async Task<bool> TryCheckHiddenAsync(CancellationToken ct)
    {
        var objectId = await ResolveObjectIdCoreAsync(ct).ConfigureAwait(false);
        return await EvalOnElementAsync<bool>(objectId,
            """
            function() {
                var style = window.getComputedStyle(this);
                return style.display === 'none' || style.visibility === 'hidden' ||
                       parseFloat(style.opacity) === 0 ||
                       this.getBoundingClientRect().width === 0;
            }
            """, null, ct).ConfigureAwait(false);
    }

    // --- IWaitCondition Polling ---

    internal async Task WaitForConditionAsync(IWaitCondition condition, WaitConditionOptions? options = null)
    {
        var timeoutMs = options?.Timeout ?? (int)(_defaultTimeout ?? ActionabilityChecker.DefaultTimeoutMs);
        var intervalMs = options?.PollingInterval ?? ActionabilityChecker.PollingIntervalMs;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_page.PageLifetimeToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        try
        {
            while (true)
            {
                cts.Token.ThrowIfCancellationRequested();
                if (await condition.EvaluateAsync(_page, options).ConfigureAwait(false))
                    return;
                await Task.Delay(intervalMs, cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw new WaitTimeoutException(
                condition: condition.ConditionName,
                timeoutDuration: TimeSpan.FromMilliseconds(timeoutMs),
                lastEvaluatedValue: null,
                message: $"WaitForCondition('{condition.ConditionName}') timed out after {timeoutMs}ms.");
        }
    }

    // --- Element Handles ---

    public async Task<IElementHandle> ElementHandleAsync(double? timeout = null)
    {
        using var cts = BuildActionCts(timeout);
        var objectId = await ResolveObjectIdCoreAsync(cts.Token).ConfigureAwait(false);
        return new ElementHandle(_page.Session, objectId);
    }

    public async Task<IReadOnlyList<IElementHandle>> ElementHandlesAsync()
    {
        using var cts = BuildActionCts(null);
        return await ResolveAllHandlesAsync(cts.Token).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<IElementHandle>> ResolveAllHandlesAsync(CancellationToken ct)
    {
        var (prefix, expression) = ParsePrefix(_selector);
        var registry = _page.ContextInternal.SelectorStrategies;

        if (!registry.TryGetStrategy(prefix, out var strategy))
            throw new InvalidOperationException($"No selector strategy registered for prefix: {prefix}");

        return await strategy!.ResolveAsync(expression, _page.GetFrameForSelectors(), _pierceShadow, ct).ConfigureAwait(false);
    }
}
