using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class ConsoleReporter(TextWriter writer, bool useColor) : IReporter, IAccessibilityReporter
{
    private const string Green = "\x1b[32m";
    private const string Red = "\x1b[31m";
    private const string Yellow = "\x1b[33m";
    private const string Gray = "\x1b[90m";
    private const string Reset = "\x1b[0m";

    public ConsoleReporter() : this(Console.Out, !Console.IsOutputRedirected) { }

    public Task OnTestRunStartAsync(TestSuiteInfo suite)
    {
        writer.WriteLine($"Running {suite.TestCount} test(s)...");
        writer.WriteLine();
        return Task.CompletedTask;
    }

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

    public Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        if (useColor)
        {
            var (color, status) = result.Passed ? (Green, "PASS") : (Red, "FAIL");
            var duration = $"{Gray}({result.DurationMs:F0}ms){Reset}";
            writer.WriteLine($"  {color}[{status}]{Reset} {result.TestName} {duration}");

            if (!result.Passed && result.ErrorMessage is not null)
                writer.WriteLine($"         {Red}{result.ErrorMessage}{Reset}");
        }
        else
        {
            var status = result.Passed ? "PASS" : "FAIL";
            writer.WriteLine($"  [{status}] {result.TestName} ({result.DurationMs:F0}ms)");

            if (!result.Passed && result.ErrorMessage is not null)
                writer.WriteLine($"         {result.ErrorMessage}");
        }

        return Task.CompletedTask;
    }

    public Task OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)
    {
        var severity = violation.Severity.ToString();
        var selector = violation.Selector is not null ? $" ({violation.Selector})" : "";

        if (useColor)
        {
            var color = violation.Severity switch
            {
                AccessibilityViolationSeverity.Error => Red,
                AccessibilityViolationSeverity.Warning => Yellow,
                _ => Gray,
            };
            writer.WriteLine($"         {color}[A11Y {severity}]{Reset} {violation.RuleId}: {violation.Message}{selector}");
        }
        else
        {
            writer.WriteLine($"         [A11Y {severity}] {violation.RuleId}: {violation.Message}{selector}");
        }

        return Task.CompletedTask;
    }

    public Task OnTestRunEndAsync(TestRunSummary summary)
    {
        writer.WriteLine();
        var total = summary.Passed + summary.Failed + summary.Skipped;

        if (useColor)
        {
            var passedText = $"{Green}{summary.Passed} passed{Reset}";
            var failedText = summary.Failed > 0
                ? $"{Red}{summary.Failed} failed{Reset}"
                : $"{summary.Failed} failed";
            writer.WriteLine($"Results: {passedText}, {failedText}, {total} total ({summary.TotalDurationMs / 1000:F1}s)");
        }
        else
        {
            writer.WriteLine($"Results: {summary.Passed} passed, {summary.Failed} failed, {total} total ({summary.TotalDurationMs / 1000:F1}s)");
        }

        return Task.CompletedTask;
    }
}
