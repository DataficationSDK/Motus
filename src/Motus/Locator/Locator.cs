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

    internal Locator(Page page, string selector, LocatorOptions? options = null)
    {
        _page = page;
        _selector = selector;
        _hasText = options?.HasText;
        _has = options?.Has;
        _hasNot = options?.HasNot;
    }

    private Locator(Page page, string selector, int? nthIndex, string? hasText, ILocator? has, ILocator? hasNot)
    {
        _page = page;
        _selector = selector;
        _nthIndex = nthIndex;
        _hasText = hasText;
        _has = has;
        _hasNot = hasNot;
    }

    // --- Core Resolution ---

    private async Task<string> ResolveObjectIdAsync()
    {
        var js = BuildResolutionExpression();

        var result = await _page.Session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: js,
                ReturnByValue: false,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            CancellationToken.None);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Locator resolution failed: {result.ExceptionDetails.Text}");

        if (result.Result.ObjectId is null ||
            result.Result.Type == "object" && result.Result.Subtype == "null")
            throw new InvalidOperationException(
                $"No element found for selector: {_selector}");

        return result.Result.ObjectId;
    }

    private string BuildResolutionExpression()
    {
        var sel = JsonEncodedText.Encode(_selector).ToString();

        if (_hasText is not null)
        {
            var txt = JsonEncodedText.Encode(_hasText).ToString();
            var qsa = "Array.from(document.querySelectorAll(\"" + sel + "\"))";
            var filter = ".filter(el=>el.textContent&&el.textContent.includes(\"" + txt + "\"))";
            var find = ".find(el=>el.textContent&&el.textContent.includes(\"" + txt + "\"))";
            return _nthIndex switch
            {
                0 => "(()=>{const e=" + qsa + ";return e" + find + "||null})()",
                -1 => "(()=>{const e=" + qsa + filter + ";return e[e.length-1]||null})()",
                int n when n > 0 => "(()=>{const e=" + qsa + filter + ";return e[" + n + "]||null})()",
                _ => "(()=>{const e=" + qsa + ";return e" + find + "||null})()"
            };
        }

        return _nthIndex switch
        {
            0 => "document.querySelectorAll(\"" + sel + "\")[0]||null",
            -1 => "(()=>{const e=document.querySelectorAll(\"" + sel + "\");return e[e.length-1]||null})()",
            int n when n > 0 => "document.querySelectorAll(\"" + sel + "\")[" + n + "]||null",
            _ => "document.querySelector(\"" + sel + "\")"
        };
    }

    private async Task<T> EvalOnElementAsync<T>(string objectId, string jsFunction, RuntimeCallArgument[]? args = null)
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
            CancellationToken.None);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Evaluation failed: {result.ExceptionDetails.Text}");

        if (result.Result.Value is JsonElement element)
            return element.Deserialize<T>()!;

        if (result.Result.Type == "undefined" ||
            (result.Result.Type == "object" && result.Result.Subtype == "null"))
            return default!;

        throw new InvalidOperationException(
            $"Cannot deserialize result of type '{result.Result.Type}'.");
    }

    private async Task EvalOnElementVoidAsync(string objectId, string jsFunction, RuntimeCallArgument[]? args = null)
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
            CancellationToken.None);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Evaluation failed: {result.ExceptionDetails.Text}");
    }

    private async Task<BoundingBox> GetBoundingBoxOrThrowAsync(string objectId)
    {
        var box = await EvalOnElementAsync<BoundingBox?>(objectId,
            """
            function() {
                var r = this.getBoundingClientRect();
                if (r.width === 0 && r.height === 0) return null;
                return { x: r.x, y: r.y, width: r.width, height: r.height };
            }
            """);

        return box ?? throw new InvalidOperationException("Element has zero size bounding box.");
    }

    // --- Chaining Properties ---

    public ILocator First => new Locator(_page, _selector, 0, _hasText, _has, _hasNot);

    public ILocator Last => new Locator(_page, _selector, -1, _hasText, _has, _hasNot);

    public ILocator Nth(int index) => new Locator(_page, _selector, index, _hasText, _has, _hasNot);

    public ILocator Filter(LocatorOptions? options = null) =>
        new Locator(_page, _selector, _nthIndex,
            options?.HasText ?? _hasText,
            options?.Has ?? _has,
            options?.HasNot ?? _hasNot);

    ILocator ILocator.Locator(string selector, LocatorOptions? options = null) =>
        new Locator(_page, _selector + " " + selector, options);

    // --- Action Methods ---

    public async Task ClickAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var box = await GetBoundingBoxOrThrowAsync(objectId);
        await _page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    public async Task DblClickAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var box = await GetBoundingBoxOrThrowAsync(objectId);
        await _page.Mouse.DblClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    public async Task FillAsync(string value, double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        await EvalOnElementVoidAsync(objectId,
            """
            function(value) {
                this.focus();
                this.value = value;
                this.dispatchEvent(new Event('input', { bubbles: true }));
                this.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """,
            [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(value))]);
    }

    public async Task ClearAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        await EvalOnElementVoidAsync(objectId,
            """
            function() {
                this.focus();
                this.value = '';
                this.dispatchEvent(new Event('input', { bubbles: true }));
                this.dispatchEvent(new Event('change', { bubbles: true }));
            }
            """);
    }

    public async Task TypeAsync(string text, KeyboardTypeOptions? options = null)
    {
        var objectId = await ResolveObjectIdAsync();
        await EvalOnElementVoidAsync(objectId, "function() { this.focus(); }");
        await _page.Keyboard.TypeAsync(text, options);
    }

    public async Task PressAsync(string key, KeyboardPressOptions? options = null)
    {
        var objectId = await ResolveObjectIdAsync();
        await EvalOnElementVoidAsync(objectId, "function() { this.focus(); }");
        await _page.Keyboard.PressAsync(key, options);
    }

    public async Task CheckAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var isChecked = await EvalOnElementAsync<bool>(objectId, "function() { return this.checked; }");
        if (!isChecked)
        {
            var box = await GetBoundingBoxOrThrowAsync(objectId);
            await _page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        }
    }

    public async Task UncheckAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var isChecked = await EvalOnElementAsync<bool>(objectId, "function() { return this.checked; }");
        if (isChecked)
        {
            var box = await GetBoundingBoxOrThrowAsync(objectId);
            await _page.Mouse.ClickAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        }
    }

    public async Task SetCheckedAsync(bool @checked, double? timeout = null)
    {
        if (@checked)
            await CheckAsync(timeout);
        else
            await UncheckAsync(timeout);
    }

    public async Task<IReadOnlyList<string>> SelectOptionAsync(params string[] values)
    {
        var objectId = await ResolveObjectIdAsync();
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
            [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(values))]);
    }

    public async Task SetInputFilesAsync(IEnumerable<FilePayload> files, double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var tempFiles = new List<string>();

        try
        {
            foreach (var file in files)
            {
                var tempPath = Path.Combine(Path.GetTempPath(), file.Name);
                await File.WriteAllBytesAsync(tempPath, file.Buffer);
                tempFiles.Add(tempPath);
            }

            await _page.Session.SendAsync(
                "DOM.setFileInputFiles",
                new DomSetFileInputFilesParams(
                    Files: tempFiles.ToArray(),
                    ObjectId: objectId),
                CdpJsonContext.Default.DomSetFileInputFilesParams,
                CdpJsonContext.Default.DomSetFileInputFilesResult,
                CancellationToken.None);
        }
        finally
        {
            foreach (var tempFile in tempFiles)
            {
                try { File.Delete(tempFile); } catch { /* best effort */ }
            }
        }
    }

    public async Task HoverAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var box = await GetBoundingBoxOrThrowAsync(objectId);
        await _page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    public async Task FocusAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        await EvalOnElementVoidAsync(objectId, "function() { this.focus(); }");
    }

    public async Task TapAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var box = await GetBoundingBoxOrThrowAsync(objectId);
        await _page.Touchscreen.TapAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    public async Task ScrollIntoViewIfNeededAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        await EvalOnElementVoidAsync(objectId,
            "function() { this.scrollIntoViewIfNeeded ? this.scrollIntoViewIfNeeded() : this.scrollIntoView({ block: 'center' }); }");
    }

    public async Task<byte[]> ScreenshotAsync(ScreenshotOptions? options = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var box = await GetBoundingBoxOrThrowAsync(objectId);

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
            CancellationToken.None);

        var bytes = Convert.FromBase64String(result.Data);

        if (options?.Path is not null)
        {
            var dir = Path.GetDirectoryName(options.Path);
            if (dir is not null)
                Directory.CreateDirectory(dir);
            await File.WriteAllBytesAsync(options.Path, bytes);
        }

        return bytes;
    }

    public async Task DispatchEventAsync(string type, object? eventInit = null)
    {
        var objectId = await ResolveObjectIdAsync();
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
            ]);
    }

    public async Task<T> EvaluateAsync<T>(string expression, object? arg = null)
    {
        var objectId = await ResolveObjectIdAsync();
        var args = arg is not null
            ? new[] { new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(arg)) }
            : (RuntimeCallArgument[]?)null;
        return await EvalOnElementAsync<T>(objectId, expression, args);
    }

    // --- Query Methods ---

    public async Task<string?> TextContentAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<string?>(objectId, "function() { return this.textContent; }");
    }

    public async Task<string> InnerTextAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<string>(objectId, "function() { return this.innerText; }");
    }

    public async Task<string> InnerHTMLAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<string>(objectId, "function() { return this.innerHTML; }");
    }

    public async Task<string?> GetAttributeAsync(string name, double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<string?>(objectId,
            "function(name) { return this.getAttribute(name); }",
            [new RuntimeCallArgument(Value: JsonSerializer.SerializeToElement(name))]);
    }

    public async Task<string> InputValueAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<string>(objectId, "function() { return this.value; }");
    }

    public async Task<BoundingBox?> BoundingBoxAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<BoundingBox?>(objectId,
            """
            function() {
                var r = this.getBoundingClientRect();
                if (r.width === 0 && r.height === 0) return null;
                return { x: r.x, y: r.y, width: r.width, height: r.height };
            }
            """);
    }

    public async Task<int> CountAsync()
    {
        var escapedSelector = JsonEncodedText.Encode(_selector).ToString();
        var result = await _page.Session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: $"""document.querySelectorAll("{escapedSelector}").length""",
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            CancellationToken.None);

        if (result.Result.Value is JsonElement element)
            return element.GetInt32();

        return 0;
    }

    public async Task<IReadOnlyList<string>> AllInnerTextsAsync()
    {
        var escapedSelector = JsonEncodedText.Encode(_selector).ToString();
        var result = await _page.Session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: $"""Array.from(document.querySelectorAll("{escapedSelector}")).map(e=>e.innerText)""",
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            CancellationToken.None);

        if (result.Result.Value is JsonElement element)
            return element.Deserialize<string[]>() ?? [];

        return [];
    }

    public async Task<IReadOnlyList<string>> AllTextContentsAsync()
    {
        var escapedSelector = JsonEncodedText.Encode(_selector).ToString();
        var result = await _page.Session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: $"""Array.from(document.querySelectorAll("{escapedSelector}")).map(e=>e.textContent||'')""",
                ReturnByValue: true,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            CancellationToken.None);

        if (result.Result.Value is JsonElement element)
            return element.Deserialize<string[]>() ?? [];

        return [];
    }

    // --- State Query Methods ---

    public async Task<bool> IsCheckedAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<bool>(objectId, "function() { return !!this.checked; }");
    }

    public async Task<bool> IsDisabledAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<bool>(objectId, "function() { return !!this.disabled; }");
    }

    public async Task<bool> IsEnabledAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<bool>(objectId, "function() { return !this.disabled; }");
    }

    public async Task<bool> IsEditableAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return await EvalOnElementAsync<bool>(objectId,
            """
            function() {
                if (this.contentEditable === 'true') return true;
                var tag = this.tagName.toLowerCase();
                if (tag === 'input' || tag === 'textarea' || tag === 'select') return !this.disabled && !this.readOnly;
                return false;
            }
            """);
    }

    public async Task<bool> IsVisibleAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
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
            """);
    }

    public async Task<bool> IsHiddenAsync(double? timeout = null)
    {
        return !await IsVisibleAsync(timeout);
    }

    // --- Waiting ---

    public async Task WaitForAsync(ElementState? state = null, double? timeout = null)
    {
        var targetState = state ?? ElementState.Visible;

        switch (targetState)
        {
            case ElementState.Attached:
            case ElementState.Visible:
                // Just resolve; throws if not found
                var objectId = await ResolveObjectIdAsync();
                if (targetState == ElementState.Visible)
                {
                    var isVisible = await EvalOnElementAsync<bool>(objectId,
                        """
                        function() {
                            var style = window.getComputedStyle(this);
                            if (style.display === 'none') return false;
                            if (style.visibility === 'hidden') return false;
                            if (parseFloat(style.opacity) === 0) return false;
                            var r = this.getBoundingClientRect();
                            return r.width > 0 && r.height > 0;
                        }
                        """);
                    if (!isVisible)
                        throw new TimeoutException($"Element '{_selector}' is not visible.");
                }
                break;

            case ElementState.Detached:
            case ElementState.Hidden:
                try
                {
                    var oid = await ResolveObjectIdAsync();
                    if (targetState == ElementState.Hidden)
                    {
                        var hidden = await EvalOnElementAsync<bool>(oid,
                            """
                            function() {
                                var style = window.getComputedStyle(this);
                                return style.display === 'none' || style.visibility === 'hidden' ||
                                       parseFloat(style.opacity) === 0 ||
                                       this.getBoundingClientRect().width === 0;
                            }
                            """);
                        if (!hidden)
                            throw new TimeoutException($"Element '{_selector}' is not hidden.");
                    }
                    else
                    {
                        // Element exists but should be detached
                        throw new TimeoutException($"Element '{_selector}' is still attached.");
                    }
                }
                catch (InvalidOperationException)
                {
                    // Element not found, which satisfies Detached and Hidden
                }
                break;
        }
    }

    // --- Element Handles ---

    public async Task<IElementHandle> ElementHandleAsync(double? timeout = null)
    {
        var objectId = await ResolveObjectIdAsync();
        return new ElementHandle(_page.Session, objectId);
    }

    public async Task<IReadOnlyList<IElementHandle>> ElementHandlesAsync()
    {
        var escapedSelector = JsonEncodedText.Encode(_selector).ToString();

        var result = await _page.Session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: $"""Array.from(document.querySelectorAll("{escapedSelector}"))""",
                ReturnByValue: false,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            CancellationToken.None);

        if (result.Result.ObjectId is null)
            return [];

        // Get array elements via properties
        var props = await _page.Session.SendAsync(
            "Runtime.getProperties",
            new RuntimeGetPropertiesParams(result.Result.ObjectId, OwnProperties: true),
            CdpJsonContext.Default.RuntimeGetPropertiesParams,
            CdpJsonContext.Default.RuntimeGetPropertiesResult,
            CancellationToken.None);

        var handles = new List<IElementHandle>();
        foreach (var prop in props.Result)
        {
            if (int.TryParse(prop.Name, out _) && prop.Value?.ObjectId is not null)
                handles.Add(new ElementHandle(_page.Session, prop.Value.ObjectId));
        }

        return handles;
    }
}
