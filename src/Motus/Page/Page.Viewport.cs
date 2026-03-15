using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    internal void SetViewportInternal(ViewportSize vp) => _viewportSize = vp;

    public async Task SetViewportSizeAsync(ViewportSize viewportSize)
    {
        _viewportSize = viewportSize;

        await _session.SendAsync(
            "Emulation.setDeviceMetricsOverride",
            new EmulationSetDeviceMetricsOverrideParams(
                Width: viewportSize.Width,
                Height: viewportSize.Height,
                DeviceScaleFactor: 1,
                Mobile: false),
            CdpJsonContext.Default.EmulationSetDeviceMetricsOverrideParams,
            CdpJsonContext.Default.EmulationSetDeviceMetricsOverrideResult,
            _pageCts.Token).ConfigureAwait(false);
    }

    // --- Locator methods ---

    public ILocator Locator(string selector, LocatorOptions? options = null)
        => new Locator(this, selector, options);

    public ILocator GetByRole(string role, string? name = null)
        => name is not null
            ? new Locator(this, $"[role=\"{role}\"][aria-label=\"{name}\"]")
            : new Locator(this, $"[role=\"{role}\"]");

    public ILocator GetByText(string text, bool? exact = null)
        => new Locator(this, "*", new LocatorOptions { HasText = text });

    public ILocator GetByLabel(string text, bool? exact = null)
        => new Locator(this, $"[aria-label=\"{text}\"]");

    public ILocator GetByPlaceholder(string text, bool? exact = null)
        => new Locator(this, $"[placeholder=\"{text}\"]");

    public ILocator GetByTestId(string testId)
        => new Locator(this, $"[data-testid=\"{testId}\"]");

    public ILocator GetByTitle(string text, bool? exact = null)
        => new Locator(this, $"[title=\"{text}\"]");

    public ILocator GetByAltText(string text, bool? exact = null)
        => new Locator(this, $"[alt=\"{text}\"]");

    public async Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null)
    {
        string js;
        if (url is not null)
        {
            var serializedUrl = System.Text.Json.JsonSerializer.Serialize(url);
            js = "(() => { const s = document.createElement('script'); s.src = " + serializedUrl + "; document.head.appendChild(s); return s; })()";
        }
        else
        {
            var serializedContent = System.Text.Json.JsonSerializer.Serialize(content ?? "");
            js = "(() => { const s = document.createElement('script'); s.textContent = " + serializedContent + "; document.head.appendChild(s); return s; })()";
        }

        var result = await _session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(Expression: js, ReturnByValue: false, AwaitPromise: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            _pageCts.Token).ConfigureAwait(false);

        if (result.Result.ObjectId is null)
            throw new InvalidOperationException("Failed to create script element.");

        return new ElementHandle(_session, result.Result.ObjectId);
    }

    public async Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null)
    {
        string js;
        if (url is not null)
        {
            var serializedUrl = System.Text.Json.JsonSerializer.Serialize(url);
            js = "(() => { const l = document.createElement('link'); l.rel = 'stylesheet'; l.href = " + serializedUrl + "; document.head.appendChild(l); return l; })()";
        }
        else
        {
            var serializedContent = System.Text.Json.JsonSerializer.Serialize(content ?? "");
            js = "(() => { const s = document.createElement('style'); s.textContent = " + serializedContent + "; document.head.appendChild(s); return s; })()";
        }

        var result = await _session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(Expression: js, ReturnByValue: false, AwaitPromise: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            _pageCts.Token).ConfigureAwait(false);

        if (result.Result.ObjectId is null)
            throw new InvalidOperationException("Failed to create style element.");

        return new ElementHandle(_session, result.Result.ObjectId);
    }

    public Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback)
        => ExposeBindingInternalAsync(name, callback);

    public async Task CloseAsync(bool? runBeforeUnload = null)
    {
        if (_isClosed)
            return;

        if (_videoRecorder is not null)
        {
            try { await _videoRecorder.StopAndFinalizeAsync().ConfigureAwait(false); }
            catch { /* best-effort */ }
        }

        try
        {
            await _context.ClosePageAsync(this, _targetId).ConfigureAwait(false);
        }
        finally
        {
            await DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task BringToFrontAsync()
    {
        await _session.SendAsync(
            "Page.bringToFront",
            CdpJsonContext.Default.PageBringToFrontResult,
            _pageCts.Token).ConfigureAwait(false);
    }

    public Task PauseAsync()
        => throw new NotSupportedException("Pause is not supported.");

    public async Task EmulateMediaAsync(string? media = null, ColorScheme? colorScheme = null)
    {
        var features = colorScheme is not null
            ? new[] { new EmulationMediaFeature("prefers-color-scheme", colorScheme.Value.ToString().ToLowerInvariant()) }
            : null;

        await _session.SendAsync(
            "Emulation.setEmulatedMedia",
            new EmulationSetEmulatedMediaParams(Media: media, Features: features),
            CdpJsonContext.Default.EmulationSetEmulatedMediaParams,
            CdpJsonContext.Default.EmulationSetEmulatedMediaResult,
            _pageCts.Token).ConfigureAwait(false);
    }
}
