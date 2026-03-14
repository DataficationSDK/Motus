namespace Motus.Runner.Services.Timeline;

public interface IStepDebugService
{
    bool IsStepMode { get; }
    bool IsPaused { get; }
    string? PendingActionType { get; }
    string? PendingSelector { get; }
    event Action? StateChanged;
    void EnableStepMode();
    void DisableStepMode();
    void Advance();
    void Resume();
    Task WaitIfPausedAsync(string actionType, string? selector, CancellationToken ct);
}
