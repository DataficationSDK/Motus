using System.Diagnostics;
using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// What an action says about itself. The rule that matters is what is left out: an agent that has
/// to read a paragraph after every click pays for it on every call, so a row appears only when the
/// action actually changed the thing it describes.
/// </summary>
[TestClass]
public class ActionReportTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [TestMethod]
    public async Task AnActionThatChangesNothing_IsStillOneLine()
    {
        var page = NewPage();
        var service = new ReportingPageService(page);

        var result = await Run(service, page);

        Assert.AreEqual("Clicked e5", TextOf(result));
    }

    [TestMethod]
    public async Task WhenThePageMoves_TheResultSaysWhereItIsNow()
    {
        var page = NewPage();
        var service = new ReportingPageService(page);

        var result = await Run(service, page, during: () =>
        {
            page.PageUrl = "https://example.test/thanks";
            page.PageTitle = "Thank you";
        });

        StringAssert.Contains(TextOf(result), "\nPage: https://example.test/thanks | Thank you");
    }

    [TestMethod]
    public async Task WhenOnlyTheTitleChanges_TheResultStillSaysSo()
    {
        var page = NewPage();
        var service = new ReportingPageService(page);

        var result = await Run(service, page, during: () => page.PageTitle = "Checkout (2 items)");

        StringAssert.Contains(TextOf(result), "\nPage: https://example.test/ | Checkout (2 items)");
    }

    [TestMethod]
    public async Task WhenTheActionAlreadyNamedThePage_TheRowIsNotRepeated()
    {
        var page = NewPage();
        var service = new ReportingPageService(page);

        var result = await Run(
            service,
            page,
            during: () =>
            {
                page.PageUrl = "https://example.test/next";
                page.PageTitle = "Next";
            },
            line: () => $"Navigated to {page.PageUrl} | {page.PageTitle}");

        Assert.AreEqual("Navigated to https://example.test/next | Next", TextOf(result));
    }

    [TestMethod]
    public async Task ErrorsLoggedByTheAction_AreCountedWithACursorThatReadsThem()
    {
        var page = NewPage();
        var console = new ConsoleService();
        console.Subscribe(page);
        var service = new ReportingPageService(page, console);

        // Logged before the action, so outside what the action is answerable for.
        page.RaiseConsole("error", "older");

        var result = await Run(service, page, during: () =>
        {
            page.RaiseConsole("error", "boom from button");
            page.RaisePageError("Error: uncaught boom");
            page.RaiseConsole("log", "chatter");
        });

        StringAssert.Contains(
            TextOf(result), "\nConsole: 1 error, 1 page error. Read them with console_messages since=2.");

        // The cursor the row printed returns the action's entries and not the one before it.
        var read = console.Read(2).Entries;
        Assert.AreEqual(3, read.Count);
        StringAssert.Contains(read[0].Text, "boom from button");
    }

    [TestMethod]
    public async Task AnActionThatOpensATab_ListsIt()
    {
        var page = NewPage();
        var opened = NewPage();
        opened.PageUrl = "https://example.test/other";
        var service = new ReportingPageService(page);

        var result = await Run(service, page, during: () => service.Tabs.Add(opened));

        StringAssert.Contains(TextOf(result), "\nNew tab opened: [1] https://example.test/other");
    }

    /// <summary>
    /// The page said it opened a window and it is in none of the listed tabs, which now means the
    /// window went away again or has not finished arriving. Saying nothing would leave the agent
    /// believing the action opened nothing at all.
    /// </summary>
    [TestMethod]
    public async Task AWindowThatIsInNoTabList_IsStillReported()
    {
        var page = NewPage();
        var elsewhere = NewPage();
        var service = new ReportingPageService(page, settle: 0);

        var result = await Run(service, page, during: () => page.RaisePopup(elsewhere));

        StringAssert.Contains(TextOf(result), "A window opened but is not among the tabs listed");
    }

    /// <summary>
    /// The tabs span every context the session holds, so a tab that opened in another one is an
    /// ordinary new tab. It is named with its context, because acting on it means going there.
    /// </summary>
    [TestMethod]
    public async Task ATabThatOpenedInAnotherContext_IsListedWithThatContext()
    {
        var page = NewPage();
        var opened = NewPage();
        opened.PageUrl = "https://example.test/elsewhere";
        var service = new ReportingPageService(page);
        service.Contexts[opened] = "work";

        var result = await Run(service, page, during: () =>
        {
            service.Tabs.Add(opened);
            page.RaisePopup(opened);
        });

        var text = TextOf(result);
        StringAssert.Contains(text, "New tab opened: [1] https://example.test/elsewhere in context 'work'");
        Assert.IsFalse(text.Contains("not among the tabs listed", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task AWindowTheTabsDoList_IsReportedAsANewTabAndNothingMore()
    {
        var page = NewPage();
        var opened = NewPage();
        opened.PageUrl = "https://example.test/popup";
        var service = new ReportingPageService(page);

        var result = await Run(service, page, during: () =>
        {
            service.Tabs.Add(opened);
            page.RaisePopup(opened);
        });

        var text = TextOf(result);
        StringAssert.Contains(text, "New tab opened: [1] https://example.test/popup");
        Assert.IsFalse(text.Contains("not among the tabs listed", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task AfterANavigation_TheResultSaysTheRefsAreGone()
    {
        var page = NewPage();
        var service = new ReportingPageService(page);
        await service.GetSnapshotService(page).TakeSnapshotAsync(Ct);

        var result = await Run(service, page, during: () => page.PageUrl = "https://example.test/next");

        StringAssert.Contains(TextOf(result), "Refs from the last snapshot no longer address this page");
    }

    [TestMethod]
    public async Task WithNoSnapshotTaken_TheStaleRefNoteIsNotPrinted()
    {
        var page = NewPage();
        var service = new ReportingPageService(page);

        var result = await Run(service, page, during: () => page.PageUrl = "https://example.test/next");

        Assert.IsFalse(TextOf(result).Contains("Refs from the last snapshot", StringComparison.Ordinal), TextOf(result));
    }

    [TestMethod]
    public async Task AFailedAction_IsLeftToSayWhyItFailed()
    {
        var page = NewPage();
        var service = new ReportingPageService(page);

        var result = await ActionRunner.RunAsync(
            service, page, Ct, _ =>
            {
                page.PageUrl = "https://example.test/next";
                return Task.FromResult(ToolResultHelper.Error("Click failed: no such element"));
            });

        Assert.AreEqual("Click failed: no such element", TextOf(result));
    }

    [TestMethod]
    public async Task WithSnapshotAsked_TheTreeComesBackWithTheAction()
    {
        var page = new FakeToolPage(new AccessibilitySnapshot(
            [new AccessibilityNode("1", "button", "Submit", null, null,
                new Dictionary<string, string?>(), [], BackendDOMNodeId: 1)],
            IgnoredCount: 0,
            DiagnosticMessage: null))
        {
            PageUrl = "https://example.test/",
        };
        var service = new ReportingPageService(page);

        var result = await ActionRunner.RunAsync(
            service, page, Ct, _ => Task.FromResult(ToolResultHelper.Text("Clicked e5")), snapshot: true);

        var text = TextOf(result);
        StringAssert.StartsWith(text, "Clicked e5");
        StringAssert.Contains(text, "Snapshot:");
        StringAssert.Contains(text, "button \"Submit\"");
    }

    [TestMethod]
    public async Task WhenADialogIsLeftOpen_TheResultSaysSoWithoutSayingItTwice()
    {
        var page = NewPage();
        var dialogs = new DialogService();
        dialogs.Subscribe(page);
        var service = new ReportingPageService(page, dialogs: dialogs);

        var result = await ActionRunner.RunAsync(service, page, Ct, async token =>
        {
            page.RaiseDialog(new FakeDialog(DialogType.Confirm, "Delete this?"));
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return ToolResultHelper.Text("Clicked e5");
        });

        // The dialog that interrupted the action is reported by the runner; the report does not
        // repeat it underneath.
        Assert.AreEqual(
            "The action opened a confirm dialog: \"Delete this?\". Call handle_dialog to accept or dismiss it. The page finishes the action once the dialog is answered, so a dialog opened from a mousedown handler means the click lands after this call returned.",
            TextOf(result));
    }

    [TestMethod]
    public async Task WithNoSettleTime_TheResultIsWrittenTheMomentTheActionReturns()
    {
        var page = NewPage();
        var service = new ReportingPageService(page, settle: 0);

        var started = Stopwatch.GetTimestamp();
        var result = await Run(service, page);

        Assert.AreEqual("Clicked e5", TextOf(result));
        Assert.IsTrue(
            Stopwatch.GetElapsedTime(started) < TimeSpan.FromMilliseconds(400),
            "a settle of zero must not wait out the default half second.");
    }

    private static Task<CallToolResult> Run(
        ReportingPageService service, FakeToolPage page, Action? during = null, Func<string>? line = null)
        => ActionRunner.RunAsync(service, page, Ct, _ =>
        {
            during?.Invoke();
            return Task.FromResult(ToolResultHelper.Text(line?.Invoke() ?? "Clicked e5"));
        });

    private static FakeToolPage NewPage()
        => new(new AccessibilitySnapshot([], 0, null)) { PageUrl = "https://example.test/" };

    private static string TextOf(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    /// <summary>
    /// A page service with the parts the report reads: the console capture, the tabs of the active
    /// context, and a browser that counts as launched, since nothing in the report may start one.
    /// </summary>
    private sealed class ReportingPageService : ActivePageService
    {
        private readonly FakeToolPage _page;

        public ReportingPageService(
            FakeToolPage page,
            ConsoleService? console = null,
            DialogService? dialogs = null,
            int? settle = null)
            : base(
                new BrowserSessionManager(new McpServerLaunchOptions
                {
                    SettleTimeout = settle,
                }),
                dialogs,
                console)
        {
            _page = page;
            Tabs = [page];
        }

        /// <summary>The open tabs, which a test adds to during an action to model a tab the page opened.</summary>
        public List<FakeToolPage> Tabs { get; }

        public override bool IsBrowserLaunched => true;

        protected override Task<IPage> ResolvePageAsync(CancellationToken cancellationToken)
            => Task.FromResult<IPage>(_page);

        protected override Task<IReadOnlyList<TabEntry>> GetOpenTabsAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<TabEntry>>(
                Tabs.Select(t => new TabEntry(t, ContextOf(t))).ToArray());

        /// <summary>
        /// The context a tab is modelled as belonging to. Everything is in the active context
        /// unless a test says otherwise, which is what a session with one context looks like.
        /// </summary>
        public Dictionary<FakeToolPage, string> Contexts { get; } = [];

        public override string GetActiveContextName() => ActiveContextName;

        public override IReadOnlyCollection<string> GetContextNames()
            => Contexts.Values.Append(ActiveContextName).Distinct().ToArray();

        /// <summary>The context the session is working in.</summary>
        public string ActiveContextName { get; set; } = BrowserSessionManager.DefaultContextName;

        private string ContextOf(FakeToolPage page)
            => Contexts.TryGetValue(page, out var name) ? name : ActiveContextName;
    }
}
