using Motus.Abstractions;

namespace Motus.Runner.Services;

public interface IScreencastService
{
    string? LatestFrameBase64 { get; }
    bool IsStreaming { get; }
    event Action<string>? FrameReceived;
    ElementHighlight? CurrentHighlight { get; }
    event Action? HighlightChanged;
    Task AttachPageAsync(IPage? page, CancellationToken ct = default);
    Task HighlightElementAsync(string? objectId, CancellationToken ct = default);
}

public sealed record ElementHighlight(
    double X,
    double Y,
    double Width,
    double Height,
    string Color = "rgba(0, 120, 212, 0.4)");
