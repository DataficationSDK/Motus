using Motus.Abstractions;

namespace Motus.Runner.Services.Coverage;

internal sealed class CoverageService : ICoverageService
{
    private CoverageData? _latest;

    public CoverageData? Latest => _latest;

    public bool HasData => _latest is not null
        && (_latest.Scripts.Count > 0 || _latest.Stylesheets.Count > 0);

    public event Action? CoverageChanged;

    public void Set(CoverageData coverage)
    {
        _latest = coverage;
        CoverageChanged?.Invoke();
    }

    public void Clear()
    {
        _latest = null;
        CoverageChanged?.Invoke();
    }
}
