using System.Diagnostics;
using System.Reflection;
using Motus.Abstractions;
using Motus.Cli.Services.Reporters;
using CliTestResult = Motus.Cli.Services.TestResult;

namespace Motus.Cli.Services;

public sealed record TestResult(string FullName, bool Passed, TimeSpan Duration, string? ErrorMessage, string? StackTrace);

public sealed record TestRunResult(int Total, int Passed, int Failed, int Skipped, TimeSpan Duration);

public sealed class TestRunner(int maxWorkers)
{
    public async Task<TestRunResult> RunAsync(List<DiscoveredTest> tests, IReporter reporter)
    {
        var suiteName = tests.Count > 0
            ? tests[0].TestClass.Assembly.GetName().Name ?? "Motus Tests"
            : "Motus Tests";

        await reporter.OnTestRunStartAsync(new TestSuiteInfo(suiteName, tests.Count));

        var sw = Stopwatch.StartNew();
        var semaphore = new SemaphoreSlim(maxWorkers);
        var results = new List<CliTestResult>();
        var lockObj = new object();

        var tasks = tests.Select(async test =>
        {
            await semaphore.WaitAsync();
            try
            {
                var testInfo = new TestInfo(test.FullName, suiteName);
                await reporter.OnTestStartAsync(testInfo);

                var result = await ExecuteTestAsync(test);
                lock (lockObj)
                {
                    results.Add(result);
                }

                var absResult = new Abstractions.TestResult(
                    result.FullName, result.Passed, result.Duration.TotalMilliseconds,
                    result.ErrorMessage, result.StackTrace);
                await reporter.OnTestEndAsync(testInfo, absResult);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);
        var runResult = new TestRunResult(results.Count, passed, failed, 0, sw.Elapsed);

        await reporter.OnTestRunEndAsync(new TestRunSummary(suiteName, passed, failed, 0, sw.Elapsed.TotalMilliseconds));
        return runResult;
    }

    private static async Task<CliTestResult> ExecuteTestAsync(DiscoveredTest test)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var instance = Activator.CreateInstance(test.TestClass)!;
            var result = test.TestMethod.Invoke(instance, null);

            if (result is Task task)
                await task;

            sw.Stop();
            return new CliTestResult(test.FullName, true, sw.Elapsed, null, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            sw.Stop();
            return new CliTestResult(test.FullName, false, sw.Elapsed, ex.InnerException.Message, ex.InnerException.StackTrace);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new CliTestResult(test.FullName, false, sw.Elapsed, ex.Message, ex.StackTrace);
        }
    }
}
