namespace Motus.Runner.Services.Timeline;

internal sealed class StepDebugService : IStepDebugService
{
    private readonly SemaphoreSlim _gate = new(0, 1);
    private volatile bool _isStepMode;
    private volatile bool _isPaused;
    private string? _pendingActionType;
    private string? _pendingSelector;

    public bool IsStepMode => _isStepMode;
    public bool IsPaused => _isPaused;
    public string? PendingActionType => _pendingActionType;
    public string? PendingSelector => _pendingSelector;

    public event Action? StateChanged;

    public void EnableStepMode()
    {
        _isStepMode = true;
        StateChanged?.Invoke();
    }

    public void DisableStepMode()
    {
        _isStepMode = false;
        _isPaused = false;
        _pendingActionType = null;
        _pendingSelector = null;
        StateChanged?.Invoke();
    }

    public void Advance()
    {
        if (_isPaused && _gate.CurrentCount == 0)
        {
            _gate.Release();
        }
    }

    public void Resume()
    {
        _isStepMode = false;
        _isPaused = false;
        _pendingActionType = null;
        _pendingSelector = null;

        if (_gate.CurrentCount == 0)
        {
            try { _gate.Release(); }
            catch (SemaphoreFullException) { }
        }

        StateChanged?.Invoke();
    }

    public async Task WaitIfPausedAsync(string actionType, string? selector, CancellationToken ct)
    {
        if (!_isStepMode)
            return;

        _pendingActionType = actionType;
        _pendingSelector = selector;
        _isPaused = true;
        StateChanged?.Invoke();

        try
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _isPaused = false;
            _pendingActionType = null;
            _pendingSelector = null;
        }
    }
}
