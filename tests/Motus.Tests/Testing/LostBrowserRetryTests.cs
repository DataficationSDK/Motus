using Motus.Testing.MSTest;
using MotusTargetClosedException = Motus.Abstractions.MotusTargetClosedException;

namespace Motus.Tests.Testing;

/// <summary>
/// Runs the retry through MSTest itself rather than through a stand-in test method.
/// </summary>
[TestClass]
public class LostBrowserRetryTests
{
    private static int s_attempts;
    private static int s_setups;

    [TestInitialize]
    public void Setup() => s_setups++;

    [MotusTestMethod]
    public void ATestWhoseBrowserWentAway_RunsAgainWithASetupOfItsOwn()
    {
        s_attempts++;

        if (s_attempts == 1)
            throw new MotusTargetClosedException("page", "A1", "CDP WebSocket disconnected.");

        // Reaching here at all is the retry. The second setup is what makes it worth having: for a
        // browser test that is where the fresh context and page come from, so a retry that skipped
        // it would run against the browser that just died.
        Assert.AreEqual(2, s_attempts, "the test should have been run a second time");
        Assert.AreEqual(2, s_setups, "the second attempt should have had its own setup");
    }
}
