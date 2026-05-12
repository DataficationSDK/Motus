using Motus.Runner.Services.Models;

namespace Motus.Runner.Services;

public sealed class TrxRerunService(
    RunnerOptions options,
    ITestSessionService session,
    TestDiscovery discovery,
    ILogger<TrxRerunService> logger)
{
    public bool IsRerunSession { get; private set; }
    public string? LastRerunTestFullName { get; private set; }
    public event Action? ViewerModeChanged;

    public async Task RerunTestAsync(string fullName, CancellationToken ct = default)
    {
        var trxTest = session.DiscoveredTests.FirstOrDefault(t => t.FullName == fullName);
        if (trxTest is null)
        {
            logger.LogWarning("Rerun requested for unknown test {FullName}", fullName);
            return;
        }

        var codeBase = trxTest.CodeBase;
        if (string.IsNullOrEmpty(codeBase) || !File.Exists(codeBase))
        {
            session.SetTestState(new TestNodeState(
                fullName,
                TestStatus.Failed,
                null,
                $"Assembly not found at {codeBase}. Build the project and try again.",
                null));
            return;
        }

        DiscoveredTest? reflected;
        try
        {
            var candidates = discovery.Discover([codeBase], fullName);
            reflected = candidates.FirstOrDefault(t => t.FullName == fullName);
        }
        catch (Exception ex)
        {
            session.SetTestState(new TestNodeState(
                fullName,
                TestStatus.Failed,
                null,
                $"Failed to load assembly {codeBase}: {ex.Message}",
                ex.StackTrace));
            return;
        }

        if (reflected?.TestClass is null || reflected.TestMethod is null)
        {
            session.SetTestState(new TestNodeState(
                fullName,
                TestStatus.Failed,
                null,
                $"Test method {fullName} not found in assembly. It may have been renamed or removed.",
                null));
            return;
        }

        // Carry the TRX CodeBase forward so a subsequent "Back to Results" + rerun still works.
        var reflectedWithCodeBase = reflected with { CodeBase = codeBase };
        session.ReplaceDiscoveredTest(reflectedWithCodeBase);

        IsRerunSession = true;
        LastRerunTestFullName = fullName;
        options.ViewerMode = ViewerMode.Runner;
        ViewerModeChanged?.Invoke();

        await session.RunTestAsync(fullName, ct);
    }

    public void BackToResults()
    {
        if (options.TrxFilePath is null)
            return;

        options.ViewerMode = ViewerMode.Trx;
        ViewerModeChanged?.Invoke();
    }
}
