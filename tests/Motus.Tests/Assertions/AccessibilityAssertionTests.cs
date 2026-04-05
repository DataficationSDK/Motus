using Motus.Abstractions;
using Motus.Assertions;
using Motus.Tests.Transport;

namespace Motus.Tests.Assertions;

[TestClass]
public class AccessibilityAssertionTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
        _browser = new Motus.Browser(_transport, _registry, process: null, tempUserDataDir: null,
                                     handleSigint: false, handleSigterm: false);
        var initTask = _browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    private async Task<Motus.Page> CreatePageAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await _browser.NewContextAsync();

        var id = 3;
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""target-1""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""session-1""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        return (Motus.Page)await context.NewPageAsync();
    }

    private static AccessibilityAuditResult MakeResult(
        params AccessibilityViolation[] violations) =>
        new(
            Violations: violations,
            PassCount: 10 - violations.Length,
            ViolationCount: violations.Length,
            Duration: TimeSpan.FromMilliseconds(50));

    private static AccessibilityViolation MakeViolation(
        string ruleId,
        AccessibilityViolationSeverity severity = AccessibilityViolationSeverity.Error,
        string? selector = null) =>
        new(
            RuleId: ruleId,
            Severity: severity,
            Message: $"Test violation for {ruleId}",
            NodeRole: "img",
            NodeName: null,
            BackendDOMNodeId: 1,
            Selector: selector);

    [TestMethod]
    public async Task ToPassAccessibilityAudit_NoViolations_Passes()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult();

        var assertions = new PageAssertions(page);
        await assertions.ToPassAccessibilityAuditAsync();
    }

    [TestMethod]
    public async Task ToPassAccessibilityAudit_WithErrorViolation_Throws()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult(
            MakeViolation("a11y-alt-text", selector: "img.hero"));

        var assertions = new PageAssertions(page);

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToPassAccessibilityAuditAsync());

        Assert.IsTrue(ex.Message.Contains("ToPassAccessibilityAudit"),
            "Message should reference the assertion name.");
        Assert.IsNotNull(ex.Actual);
        Assert.IsTrue(ex.Actual.Contains("a11y-alt-text"),
            "Actual should contain the violation rule ID.");
        Assert.IsTrue(ex.Actual.Contains("img.hero"),
            "Actual should contain the violation selector.");
    }

    [TestMethod]
    public async Task ToPassAccessibilityAudit_WarningOnly_FailsByDefault()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult(
            MakeViolation("a11y-heading-hierarchy", AccessibilityViolationSeverity.Warning));

        var assertions = new PageAssertions(page);

        await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToPassAccessibilityAuditAsync());
    }

    [TestMethod]
    public async Task ToPassAccessibilityAudit_WarningOnly_PassesWhenExcluded()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult(
            MakeViolation("a11y-heading-hierarchy", AccessibilityViolationSeverity.Warning));

        var assertions = new PageAssertions(page);

        await assertions.ToPassAccessibilityAuditAsync(
            opts => opts.IncludeWarnings = false);
    }

    [TestMethod]
    public async Task ToPassAccessibilityAudit_InfoSeverity_DoesNotFail()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult(
            MakeViolation("a11y-info-rule", AccessibilityViolationSeverity.Info));

        var assertions = new PageAssertions(page);
        await assertions.ToPassAccessibilityAuditAsync();
    }

    [TestMethod]
    public async Task ToPassAccessibilityAudit_SkipRules_FiltersViolation()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult(
            MakeViolation("a11y-alt-text"),
            MakeViolation("a11y-color-contrast"));

        var assertions = new PageAssertions(page);

        // Skip the alt-text rule; should still fail because of color-contrast
        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToPassAccessibilityAuditAsync(
                opts => opts.SkipRules("a11y-alt-text")));

        Assert.IsTrue(ex.Actual!.Contains("a11y-color-contrast"));
        Assert.IsFalse(ex.Actual.Contains("a11y-alt-text"),
            "Skipped rule should not appear in violations.");
    }

    [TestMethod]
    public async Task ToPassAccessibilityAudit_SkipAllViolations_Passes()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult(
            MakeViolation("a11y-alt-text"));

        var assertions = new PageAssertions(page);

        await assertions.ToPassAccessibilityAuditAsync(
            opts => opts.SkipRules("a11y-alt-text"));
    }

    [TestMethod]
    public async Task Not_ToPassAccessibilityAudit_PassesWhenViolationsExist()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult(
            MakeViolation("a11y-alt-text"));

        var assertions = new PageAssertions(page);
        await assertions.Not.ToPassAccessibilityAuditAsync();
    }

    [TestMethod]
    public async Task Not_ToPassAccessibilityAudit_ThrowsWhenNoViolations()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult();

        var assertions = new PageAssertions(page);

        await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.Not.ToPassAccessibilityAuditAsync());
    }

    [TestMethod]
    public async Task ViolationMessage_IncludesFormattedDetails()
    {
        var page = await CreatePageAsync();
        page.LastAccessibilityAudit = MakeResult(
            MakeViolation("a11y-empty-button", selector: "button#submit"),
            MakeViolation("a11y-empty-link", AccessibilityViolationSeverity.Error, "a.nav"));

        var assertions = new PageAssertions(page);

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToPassAccessibilityAuditAsync());

        Assert.IsTrue(ex.Actual!.Contains("[Error]"), "Should contain severity label.");
        Assert.IsTrue(ex.Actual.Contains("a11y-empty-button"), "Should contain first rule ID.");
        Assert.IsTrue(ex.Actual.Contains("a11y-empty-link"), "Should contain second rule ID.");
        Assert.IsTrue(ex.Actual.Contains("button#submit"), "Should contain first selector.");
        Assert.IsTrue(ex.Actual.Contains("a.nav"), "Should contain second selector.");
        Assert.IsTrue(ex.Actual.Contains("2 violation(s)"), "Should contain violation count.");
    }
}
