using System.Diagnostics;
using System.Reflection;
using Motus.Cli.Services.Reporters;

namespace Motus.Cli.Services;

public sealed record TestResult(string FullName, bool Passed, TimeSpan Duration, string? ErrorMessage, string? StackTrace);

public sealed record TestRunResult(int Total, int Passed, int Failed, int Skipped, TimeSpan Duration);

public sealed class TestRunner(int maxWorkers)
{
    public async Task<TestRunResult> RunAsync(List<DiscoveredTest> tests, ITestReporter reporter)
    {
        await reporter.OnRunStartedAsync(tests.Count);

        var sw = Stopwatch.StartNew();
        var semaphore = new SemaphoreSlim(maxWorkers);
        var results = new List<TestResult>();
        var lockObj = new object();

        var tasks = tests.Select(async test =>
        {
            await semaphore.WaitAsync();
            try
            {
                var result = await ExecuteTestAsync(test);
                lock (lockObj)
                {
                    results.Add(result);
                }
                await reporter.OnTestCompletedAsync(result);
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

        await reporter.OnRunCompletedAsync(runResult);
        return runResult;
    }

    private static async Task<TestResult> ExecuteTestAsync(DiscoveredTest test)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var instance = Activator.CreateInstance(test.TestClass)!;
            var result = test.TestMethod.Invoke(instance, null);

            if (result is Task task)
                await task;

            sw.Stop();
            return new TestResult(test.FullName, true, sw.Elapsed, null, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            sw.Stop();
            return new TestResult(test.FullName, false, sw.Elapsed, ex.InnerException.Message, ex.InnerException.StackTrace);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestResult(test.FullName, false, sw.Elapsed, ex.Message, ex.StackTrace);
        }
    }
}
