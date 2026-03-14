using Motus.Abstractions;

namespace Motus.Runner.Services;

public static class RunnerPageBridge
{
    internal static event Action<IPage?>? PageActivated;

    public static void SetActivePage(IPage? page)
    {
        PageActivated?.Invoke(page);
    }
}
