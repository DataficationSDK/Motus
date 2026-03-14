using Motus.Abstractions;

namespace Motus.Runner.Services;

public static class RunnerPageBridge
{
    internal static event Action<IPage?>? PageActivated;

    /// <summary>
    /// Global hook invoked by the Motus engine whenever a new page is created
    /// in any browser context. Set by RunnerHost to automatically bridge
    /// test-created pages into the visual runner.
    /// </summary>
    public static Action<IPage>? GlobalPageCreatedHook { get; set; }

    public static void SetActivePage(IPage? page)
    {
        PageActivated?.Invoke(page);
    }
}
