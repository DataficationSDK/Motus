using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Motus.Abstractions;

namespace Motus.Testing.MSTest;

/// <summary>
/// Base class for MSTest browser tests. Shares a single browser across all tests
/// in the assembly and creates an isolated context per test.
/// Compatible with <c>[Parallelize]</c>.
/// </summary>
/// <remarks>
/// The shared browser launches when the first test needs it and closes when the test run ends,
/// so no assembly setup is required. Call <see cref="LaunchBrowserAsync"/> from an
/// <c>[AssemblyInitialize]</c> method only to launch it with options set in code, and
/// <see cref="CloseBrowserAsync"/> from <c>[AssemblyCleanup]</c> to close it at a point of your
/// choosing.
/// </remarks>
public abstract class MotusTestBase
{
    private static readonly BrowserFixture s_fixture = new();

    // One browser for each class that overrides LaunchOptions, keyed by the class that declares the
    // override so every test class inheriting it shares one browser.
    private static readonly ConcurrentDictionary<Type, BrowserFixture> s_overrideFixtures = new();
    private static readonly ConcurrentDictionary<Type, Type?> s_overrideOwners = new();
    private const string LaunchOptionsGetterName = "get_" + nameof(LaunchOptions);

    private static LaunchOptions? s_defaultLaunchOptions;

    private BrowserFixture? _fixture;
    private IBrowserContext? _context;
    private IPage? _page;
    private FailureTracing? _failureTracing;

    /// <summary>
    /// Override to launch the browser for this class with its own options.
    /// </summary>
    /// <remarks>
    /// A class that returns options here gets a browser of its own, separate from the shared one,
    /// and that browser is shared by every test class inheriting the override. It is launched with
    /// the options returned by the first test to need it. Returning null uses the shared browser.
    /// Options that apply to the whole run belong in motus.config.json instead.
    /// </remarks>
    protected virtual LaunchOptions? LaunchOptions => null;

    /// <summary>
    /// Override to customize per-test context options.
    /// Default viewport is 1024x768.
    /// </summary>
    protected virtual ContextOptions? ContextOptions => new()
    {
        Viewport = new ViewportSize(1024, 768),
    };

    /// <summary>
    /// The browser context for the current test.
    /// </summary>
    protected IBrowserContext Context => _context ?? throw new InvalidOperationException(
        "Context not available. Ensure [TestInitialize] has run.");

    /// <summary>
    /// The page for the current test.
    /// </summary>
    protected IPage Page => _page ?? throw new InvalidOperationException(
        "Page not available. Ensure [TestInitialize] has run.");

    /// <summary>
    /// MSTest test context, used to detect test outcome for failure tracing.
    /// </summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Optional. Launches the shared browser now with the given options; without it the browser
    /// launches with the configured defaults when the first test needs it. Call it from an
    /// <c>[AssemblyInitialize]</c> method, before any test runs: a browser already running is
    /// closed and replaced.
    /// </summary>
    public static async Task LaunchBrowserAsync(LaunchOptions? options = null)
    {
        s_defaultLaunchOptions = options;
        await s_fixture.InitializeAsync(options).ConfigureAwait(false);
    }

    /// <summary>
    /// Optional. Closes the shared browser, and any browser launched for a class that overrides
    /// <see cref="LaunchOptions"/>. Without it they are closed when the test process exits.
    /// Call it from an <c>[AssemblyCleanup]</c> method. A test that runs afterwards launches
    /// the browser again.
    /// </summary>
    public static async Task CloseBrowserAsync()
    {
        await s_fixture.DisposeAsync().ConfigureAwait(false);

        foreach (var fixture in s_overrideFixtures.Values)
            await fixture.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The browser shared by every test class that does not override <see cref="LaunchOptions"/>.
    /// </summary>
    internal static BrowserFixture SharedFixture => s_fixture;

    /// <summary>
    /// The fixture that serves this test class, and the options to launch it with.
    /// </summary>
    internal BrowserFixture ResolveFixture(out LaunchOptions? options)
    {
        var owner = s_overrideOwners.GetOrAdd(GetType(), FindLaunchOptionsOwner);
        options = owner is null ? null : LaunchOptions;

        if (options is null)
        {
            options = s_defaultLaunchOptions;
            return s_fixture;
        }

        return s_overrideFixtures.GetOrAdd(owner!, _ => new BrowserFixture());
    }

    /// <summary>
    /// The most derived class that overrides <see cref="LaunchOptions"/>, or null when none does.
    /// </summary>
    /// <remarks>
    /// Keyed by class rather than by the options themselves: a getter that builds its options
    /// afresh returns a new instance on every call, and the lists inside them compare by
    /// reference, so equal options would not compare equal.
    /// </remarks>
    internal static Type? FindLaunchOptionsOwner(Type testClass)
    {
        for (var type = testClass; type is not null && type != typeof(MotusTestBase); type = type.BaseType)
        {
            var getter = type.GetMethod(
                LaunchOptionsGetterName,
                BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                Type.EmptyTypes);

            // A getter whose base definition is its own is a new property hiding this one, not an
            // override, and the base class never calls it.
            if (getter is not null && getter.GetBaseDefinition().DeclaringType == typeof(MotusTestBase))
                return type;
        }

        return null;
    }

    [TestInitialize]
    public async Task MotusTestInitialize()
    {
        // The first test to need a browser launches it; the rest find it running. This sits
        // outside the retry below so that a launch failure is not attempted three times over.
        var fixture = ResolveFixture(out var launchOptions);
        await fixture.EnsureInitializedAsync(launchOptions).ConfigureAwait(false);
        _fixture = fixture;

        // If Chrome crashed during a previous test, the fixture auto-restarts.
        // Retry context+page creation to ride through the restart window.
        const int maxAttempts = 3;
        for (int attempt = 1; ; attempt++)
        {
            IBrowserContext? context = null;
            try
            {
                context = await fixture.NewContextAsync(ContextOptions).ConfigureAwait(false);
                _page = await context.NewPageAsync().ConfigureAwait(false);
                _context = context;
                break;
            }
            catch
            {
                // A context created before the failure holds one of the fixture's slots, and the
                // fixture hands out a fixed few. Dropping it without closing it retires that slot
                // for the rest of the run, and a run that has retired them all waits here forever.
                if (context is not null)
                {
                    try { await fixture.CloseContextAsync(context).ConfigureAwait(false); }
                    catch { /* the browser it belonged to is already gone */ }
                }

                _context = null;
                _page = null;

                if (attempt >= maxAttempts)
                    throw;

                // Give the browser fixture time to restart Chrome.
                await Task.Delay(1000 * attempt).ConfigureAwait(false);
            }
        }

        var testMethodName = TestContext?.TestName ?? TestMethodNameContext.Current;

        _failureTracing = new FailureTracing(FailureArtifactLabel(testMethodName));
        await _failureTracing.StartIfEnabledAsync(_context).ConfigureAwait(false);

        var methodInfo = testMethodName is not null
            ? GetType().GetMethod(testMethodName, BindingFlags.Public | BindingFlags.Instance)
            : null;
        var methodAttr = methodInfo?.GetCustomAttribute<PerformanceBudgetAttribute>();
        var classAttr = GetType().GetCustomAttribute<PerformanceBudgetAttribute>();
        var activeAttr = methodAttr ?? classAttr;
        var budget = activeAttr?.ToBudget();
        PerformanceBudgetContext.Push(budget);
        PerformanceBudgetContext.SetBudget(_page, budget);
    }

    [TestCleanup]
    public async Task MotusTestCleanup()
    {
        if (_page is not null)
            PerformanceBudgetContext.ClearBudget(_page);
        PerformanceBudgetContext.Clear();

        if (_context is not null)
        {
            try
            {
                var testFailed = TestContext?.CurrentTestOutcome != UnitTestOutcome.Passed;
                if (_failureTracing is not null)
                {
                    await _failureTracing.StopAsync(_context, testFailed).ConfigureAwait(false);
                    ReportFailureArtifacts(_failureTracing);
                }
            }
            catch (Exception)
            {
                // Browser may have crashed or disconnected; swallow so we don't
                // mask the original test failure with a cleanup exception.
            }

            try
            {
                // Always reached, even when tracing above failed: this is the only place the
                // context's slot goes back to the fixture, and a slot that never comes back is one
                // fewer test that can ever start.
                await (_fixture ?? s_fixture).CloseContextAsync(_context).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The context went with the browser it belonged to.
            }
            finally
            {
                _context = null;
                _page = null;
            }
        }
    }

    /// <summary>
    /// Names the test's failure artifacts <c>{Class}.{Method}</c>, after the runtime type so that
    /// a test method inherited from a shared base is named for the class that ran it.
    /// </summary>
    private string FailureArtifactLabel(string? testMethodName)
        => testMethodName is null ? GetType().Name : $"{GetType().Name}.{testMethodName}";

    /// <summary>
    /// Attaches what was saved for a failed test to its result and says where it went, so the
    /// trace is one click away in the test explorer and in a TRX file.
    /// </summary>
    private void ReportFailureArtifacts(FailureTracing tracing)
    {
        if (TestContext is null)
            return;

        if (tracing.TracePath is not null)
            ReportFailureArtifact(tracing.TracePath, "trace");

        foreach (var path in tracing.ScreenshotPaths)
            ReportFailureArtifact(path, "screenshot");
    }

    private void ReportFailureArtifact(string path, string kind)
    {
        try
        {
            TestContext.AddResultFile(path);
        }
        catch (Exception)
        {
            // The file is still on disk; failing to attach it must not mask the test's own failure.
        }

        try
        {
            TestContext.WriteLine($"Motus: failure {kind} saved to {path}");
        }
        catch (Exception)
        {
            // As above: reporting is a courtesy, never a second failure.
        }
    }
}
