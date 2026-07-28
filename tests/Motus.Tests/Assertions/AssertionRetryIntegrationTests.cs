using Motus.Abstractions;
using Motus.Assertions;

namespace Motus.Tests.Assertions;

/// <summary>
/// Pins what a retrying assertion does when the thing it is polling stops answering.
/// </summary>
/// <remarks>
/// A retrying assertion polls through a target that has gone away, because a navigation destroys
/// the execution context the previous attempt ran in and the next attempt succeeds against the new
/// document. A browser that has died looks identical for one poll and never recovers, so the two
/// have to be told apart by whether anything was ever evaluated rather than by the error alone.
/// Getting that wrong in either direction is silent: report loss too eagerly and an assertion that
/// merely spanned a navigation is retried as infrastructure, report it too late and a dead browser
/// is recorded as a verdict the test never reached.
///
/// These run against a real browser because the distinction only exists there. The rest of the
/// assertion tests drive the comparison logic and never open one.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class AssertionRetryIntegrationTests
{
    private const string PageWithInput = "data:text/html,<html><body><p>first</p><input placeholder='target'></body></html>";

    private const string PageWithDisabledInput = "data:text/html,<html><body><input placeholder='target' disabled></body></html>";

    private IBrowser? _browser;

    [TestInitialize]
    public async Task Setup()
    {
        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    [TestMethod]
    public async Task AssertionThatSpansANavigationStillPasses()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(PageWithDisabledInput);

        // The element is present on both documents, so what this measures is the execution context
        // being torn down and rebuilt underneath a running assertion, and nothing else. It starts
        // disabled so the assertion cannot be satisfied before the navigation begins: a version
        // that passes on its first poll would still be green with the retry loop deleted.
        var assertion = Expect.That(page.GetByPlaceholder("target"))
            .ToBeEditableAsync(new AssertionOptions { Timeout = 10_000 });

        await page.GotoAsync(PageWithInput);

        // Passing is the whole point: the target closing mid-flight is a step on the way to an
        // answer here, not a failure, and not a lost browser.
        await assertion;
    }

    [TestMethod]
    public async Task AssertionAgainstALostBrowserReportsTheLoss()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(PageWithInput);

        await _browser.CloseAsync();
        _browser = null;

        try
        {
            await Expect.That(page.GetByPlaceholder("target"))
                .ToBeEditableAsync(new AssertionOptions { Timeout = 2_000 });

            Assert.Fail("Expected the assertion to report the browser going away.");
        }
        catch (Exception ex) when (ex is not AssertFailedException)
        {
            // Both retry paths ask BrowserFailure, so this is the property that matters rather
            // than the exact type: an assertion exception here means neither of them re-runs the
            // test, and a browser that died is recorded as a verdict about the page.
            Assert.IsTrue(
                BrowserFailure.IsBrowserLost(ex),
                $"Expected a browser-loss failure, got {ex.GetType().Name}: {ex.Message}");
        }
    }

    [TestMethod]
    public async Task AssertionThatSimplyDoesNotHoldIsStillAnAssertionFailure()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(PageWithDisabledInput);

        try
        {
            await Expect.That(page.GetByPlaceholder("target"))
                .ToBeEditableAsync(new AssertionOptions { Timeout = 2_000 });

            Assert.Fail("Expected the assertion to fail against a disabled input.");
        }
        catch (MotusAssertionException ex)
        {
            // The live browser answered every time, so this is a verdict the test did reach and
            // must not be mistaken for the browser going away.
            Assert.IsFalse(
                BrowserFailure.IsBrowserLost(ex),
                "A failing assertion must not be reported as a lost browser.");
        }
    }
}
