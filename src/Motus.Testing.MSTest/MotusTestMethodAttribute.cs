using Microsoft.VisualStudio.TestTools.UnitTesting;
#if MSTEST_V4
using System.Runtime.CompilerServices;
#endif

// Motus has a test result of its own, for the runs its own runner reports. This file is about
// MSTest's, so the one name it borrows from Motus is named on its own.
using BrowserFailure = Motus.Abstractions.BrowserFailure;

namespace Motus.Testing.MSTest;

/// <summary>
/// A test method that is run again when the browser goes away underneath it.
/// </summary>
/// <remarks>
/// A browser that dies mid-test takes the verdict with it. Nothing was established about the page,
/// and what the run reports is a connection closing rather than anything the test set out to check.
/// Browsers do occasionally die, more often on shared build hardware than on a desk, and a red
/// build that a second run turns green is how a team learns to stop reading red builds.
///
/// Only a lost browser is run again. A failed assertion is a result, and repeating it until it
/// agrees would hide the very thing the suite exists to find.
///
/// The attempt that follows gets a browser of its own: the fixture starts replacing the dead one
/// the moment it sees the disconnect, and <see cref="MotusTestBase"/> waits for that to finish
/// while building the next context.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public class MotusTestMethodAttribute : TestMethodAttribute
{
    private readonly TestMethodAttribute? _inner;

#if MSTEST_V4
    /// <summary>
    /// Applies to a test method directly, in place of <c>[TestMethod]</c>.
    /// </summary>
    /// <remarks>
    /// The caller's file and line are filled in by the compiler and passed on to MSTest, which uses
    /// them to take the test explorer to the test's source.
    /// </remarks>
    public MotusTestMethodAttribute([CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = -1)
        : base(callerFilePath, callerLineNumber)
    {
    }

    /// <summary>
    /// Wraps another kind of test method, so what that one does is kept and only the retry is added.
    /// </summary>
    /// <remarks>
    /// Called from code rather than written on a method, so the source location is the wrapped
    /// attribute's, not this constructor's caller.
    /// </remarks>
#pragma warning disable MSTEST0057 // the location comes from the wrapped attribute
    public MotusTestMethodAttribute(TestMethodAttribute? inner)
        : base(inner?.DeclaringFilePath ?? "", inner?.DeclaringLineNumber ?? -1)
#pragma warning restore MSTEST0057
    {
        _inner = inner;
    }
#else
    /// <summary>
    /// Applies to a test method directly, in place of <c>[TestMethod]</c>.
    /// </summary>
    public MotusTestMethodAttribute()
    {
    }

    /// <summary>
    /// Wraps another kind of test method, so what that one does is kept and only the retry is added.
    /// </summary>
    public MotusTestMethodAttribute(TestMethodAttribute? inner)
    {
        _inner = inner;
    }
#endif

    /// <summary>
    /// How many further attempts a lost browser is worth. Zero runs the test once, whatever happens.
    /// </summary>
    public int Retries { get; set; } = 1;

    // MSTest 4 runs a test through ExecuteAsync, and MSTest 3 through Execute, which 4 removed.
#if MSTEST_V4
    /// <inheritdoc />
    public override async Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
    {
        // No ConfigureAwait(false): each attempt resumes on the context MSTest ran the first one
        // on, which an attribute being wrapped may depend on.
        var results = await RunAsync(testMethod);

        for (int attempt = 1; attempt <= Retries && LostTheBrowser(results); attempt++)
        {
            AnnounceRetry(testMethod, attempt);
            results = await RunAsync(testMethod);
        }

        return results;
    }

    private Task<TestResult[]> RunAsync(ITestMethod testMethod)
        => _inner is not null ? _inner.ExecuteAsync(testMethod) : base.ExecuteAsync(testMethod);
#else
    /// <inheritdoc />
    public override TestResult[] Execute(ITestMethod testMethod)
    {
        var results = Run(testMethod);

        for (int attempt = 1; attempt <= Retries && LostTheBrowser(results); attempt++)
        {
            AnnounceRetry(testMethod, attempt);
            results = Run(testMethod);
        }

        return results;
    }

    private TestResult[] Run(ITestMethod testMethod)
        => _inner is not null ? _inner.Execute(testMethod) : base.Execute(testMethod);
#endif

    private static bool LostTheBrowser(TestResult[]? results)
        => results is not null && Array.Exists(results, result => BrowserFailure.IsBrowserLost(result.TestFailureException));

    /// <summary>
    /// Says a retry happened, in the shape the Motus runner uses, so a test that only passes on a
    /// second attempt is visible in the log rather than silently green.
    /// </summary>
    private void AnnounceRetry(ITestMethod testMethod, int attempt)
        => Console.Error.WriteLine(
            $"  [RETRY] {testMethod.TestClassName}.{testMethod.TestMethodName} "
            + $"(lost browser, attempt {attempt + 1}/{Retries + 1})");
}
