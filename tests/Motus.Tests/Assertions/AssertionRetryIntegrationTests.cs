using System.Diagnostics;
using Motus.Abstractions;
using Motus.Assertions;

namespace Motus.Tests.Assertions;

/// <summary>
/// Pins what a retrying assertion does when the thing it is polling stops answering, or is not
/// there yet.
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
/// An element that has not rendered yet is the other thing a poll can run into, and the reason a
/// retrying assertion exists: most assertions wait for it, while the few that absence answers
/// outright (hidden, not visible, detached) pass on the first poll instead of running out the
/// clock.
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

    // Renders its content a second after load, the way a page does after a form submit or a click
    // that fetches something, so an assertion made straight after navigating starts before the
    // elements exist.
    private const string PageThatRendersLater =
        "data:text/html,<html><body><p>start</p><script>setTimeout(() => {"
        + "const h = document.createElement('h1'); h.id = 'done'; h.textContent = 'Saved'; h.title = 'status';"
        + "const i = document.createElement('input'); i.id = 'field'; i.value = 'ready';"
        + "document.body.append(h, i); }, 1000)</script></body></html>";

    private const string PageWithNothingToFind = "data:text/html,<html><body><p>start</p></body></html>";

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

    [TestMethod]
    [DataRow("ToBeVisible")]
    [DataRow("ToHaveText")]
    [DataRow("ToContainText")]
    [DataRow("ToHaveAttribute")]
    [DataRow("ToBeEnabled")]
    [DataRow("ToBeEditable")]
    [DataRow("ToHaveValue")]
    [DataRow("Not.ToBeHidden")]
    public async Task AssertionWaitsForAnElementThatRendersLater(string assertion)
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(PageThatRendersLater);

        // Checked first so the test cannot pass by starting after the element arrived, which would
        // leave the retry loop untested.
        Assert.AreEqual(0, await page.Locator("#done, #field").CountAsync(),
            "The elements rendered before the assertion started.");

        var options = new AssertionOptions { Timeout = 10_000 };
        var heading = Expect.That(page.Locator("#done"));
        var field = Expect.That(page.Locator("#field"));

        await (assertion switch
        {
            "ToBeVisible" => heading.ToBeVisibleAsync(options),
            "ToHaveText" => heading.ToHaveTextAsync("Saved", options),
            "ToContainText" => heading.ToContainTextAsync("Sav", options),
            "ToHaveAttribute" => heading.ToHaveAttributeAsync("title", "status", options),
            "ToBeEnabled" => field.ToBeEnabledAsync(options),
            "ToBeEditable" => field.ToBeEditableAsync(options),
            "ToHaveValue" => field.ToHaveValueAsync("ready", options),
            "Not.ToBeHidden" => heading.Not.ToBeHiddenAsync(options),
            _ => throw new ArgumentOutOfRangeException(nameof(assertion), assertion, null),
        });
    }

    [TestMethod]
    [DataRow("ToBeHidden")]
    [DataRow("Not.ToBeVisible")]
    [DataRow("ToBeDetached")]
    [DataRow("Not.ToBeAttached")]
    [DataRow("ToHaveCount(0)")]
    public async Task AssertionThatAbsenceAnswersPassesAtOnce(string assertion)
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(PageWithNothingToFind);

        var options = new AssertionOptions { Timeout = 10_000 };
        var missing = Expect.That(page.Locator("#nope"));

        var elapsed = Stopwatch.StartNew();
        await (assertion switch
        {
            "ToBeHidden" => missing.ToBeHiddenAsync(options),
            "Not.ToBeVisible" => missing.Not.ToBeVisibleAsync(options),
            "ToBeDetached" => missing.ToBeDetachedAsync(options),
            "Not.ToBeAttached" => missing.Not.ToBeAttachedAsync(options),
            "ToHaveCount(0)" => missing.ToHaveCountAsync(0, options),
            _ => throw new ArgumentOutOfRangeException(nameof(assertion), assertion, null),
        });
        elapsed.Stop();

        // Passing is not enough: an assertion that polled to its timeout and then passed would be
        // just as green and ten seconds slower. Absence is the answer here, so the first poll is.
        Assert.IsTrue(elapsed.ElapsedMilliseconds < 2_000,
            $"{assertion} took {elapsed.ElapsedMilliseconds}ms against a 10000ms timeout.");
    }

    [TestMethod]
    public async Task AssertionOnAnElementThatNeverRendersFailsAtTheTimeout()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(PageWithNothingToFind);

        var elapsed = Stopwatch.StartNew();
        try
        {
            await Expect.That(page.Locator("#nope"))
                .ToHaveTextAsync("Saved", new AssertionOptions { Timeout = 1_500 });

            Assert.Fail("Expected the assertion to fail against an element that never renders.");
        }
        catch (MotusAssertionException ex)
        {
            elapsed.Stop();

            // It waited the whole timeout for the element rather than giving up on the first poll,
            // and the failure says the element was missing rather than naming a value it never had.
            Assert.IsTrue(elapsed.ElapsedMilliseconds >= 1_400,
                $"Failed after {elapsed.ElapsedMilliseconds}ms, before the 1500ms timeout.");
            Assert.AreEqual("<element not found>", ex.Actual);
            StringAssert.Contains(ex.Message, "Received: <element not found>");
            StringAssert.Contains(ex.Message, "Selector: #nope");
            Assert.IsFalse(BrowserFailure.IsBrowserLost(ex));
        }
    }

    [TestMethod]
    public async Task VisibilityQueriesAnswerForAnElementThatIsNotThere()
    {
        var page = await _browser!.NewPageAsync();
        await page.GotoAsync(PageWithNothingToFind);

        var missing = page.Locator("#nope");

        Assert.IsFalse(await missing.IsVisibleAsync());
        Assert.IsTrue(await missing.IsHiddenAsync());
    }
}
