namespace Motus.Runner.Services.Timeline;

public sealed record TimelineEntry(
    int Index,
    DateTime Timestamp,
    string ActionType,
    string? Selector,
    TimeSpan Duration,
    byte[]? ScreenshotBefore,
    byte[]? ScreenshotAfter,
    bool HasError,
    string? ErrorMessage,
    IReadOnlyList<NetworkCapture> NetworkRequests,
    IReadOnlyList<ConsoleCapture> ConsoleMessages,
    string? TestName = null);
