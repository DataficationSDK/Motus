using System.Diagnostics;
using System.Reflection;
using Motus.Runner.Services.Models;

namespace Motus.Runner.Services;

public sealed class TestExecutionService(ILogger<TestExecutionService> logger)
{
    private const int MaxWorkers = 4;

    public async Task ExecuteAsync(
        List<DiscoveredTest> tests,
        Action<TestNodeState> onStateChanged,
        CancellationToken ct)
    {
        var semaphore = new SemaphoreSlim(MaxWorkers);

        var tasks = tests.Select(async test =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                onStateChanged(new TestNodeState(test.FullName, TestStatus.Running, null, null, null));

                var result = await ExecuteTestAsync(test);
                onStateChanged(result);
            }
            catch (OperationCanceledException)
            {
                onStateChanged(new TestNodeState(test.FullName, TestStatus.Skipped, null, "Cancelled", null));
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Test run cancelled");
        }
    }

    private static async Task<TestNodeState> ExecuteTestAsync(DiscoveredTest test)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var instance = Activator.CreateInstance(test.TestClass)!;
            var result = test.TestMethod.Invoke(instance, null);

            if (result is Task task)
                await task;

            sw.Stop();
            return new TestNodeState(test.FullName, TestStatus.Passed, sw.Elapsed, null, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            sw.Stop();
            return new TestNodeState(test.FullName, TestStatus.Failed, sw.Elapsed, ex.InnerException.Message, ex.InnerException.StackTrace);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestNodeState(test.FullName, TestStatus.Failed, sw.Elapsed, ex.Message, ex.StackTrace);
        }
    }
}
