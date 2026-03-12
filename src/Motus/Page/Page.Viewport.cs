using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
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
            _pageCts.Token);
    }

    // --- Stubbed methods ---

    public ILocator Locator(string selector, LocatorOptions? options = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByRole(string role, string? name = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByText(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByLabel(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByPlaceholder(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByTestId(string testId)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByTitle(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByAltText(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public Task RouteAsync(string urlPattern, Func<IRoute, Task> handler)
        => throw new NotImplementedException("Routing is not yet implemented (Phase 1J).");

    public Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null)
        => throw new NotImplementedException("Routing is not yet implemented (Phase 1J).");

    public Task<IRequest> WaitForRequestAsync(string urlPattern, double? timeout = null)
        => throw new NotImplementedException("Request interception is not yet implemented (Phase 1J).");

    public Task<IResponse> WaitForResponseAsync(string urlPattern, double? timeout = null)
        => throw new NotImplementedException("Response interception is not yet implemented (Phase 1J).");

    public Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null)
        => throw new NotImplementedException("AddScriptTag is not yet implemented (Phase 1I).");

    public Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null)
        => throw new NotImplementedException("AddStyleTag is not yet implemented (Phase 1I).");

    public Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback)
        => ExposeBindingInternalAsync(name, callback);

    public async Task CloseAsync(bool? runBeforeUnload = null)
    {
        if (_isClosed)
            return;

        try
        {
            await _context.ClosePageAsync(this, _targetId);
        }
        finally
        {
            await DisposeAsync();
        }
    }

    public async Task BringToFrontAsync()
    {
        await _session.SendAsync(
            "Page.bringToFront",
            CdpJsonContext.Default.PageBringToFrontResult,
            _pageCts.Token);
    }

    public Task PauseAsync()
        => throw new NotSupportedException("Pause is not supported.");

    public Task EmulateMediaAsync(string? media = null, ColorScheme? colorScheme = null)
        => throw new NotImplementedException("EmulateMedia is not yet implemented (Phase 1I).");
}
