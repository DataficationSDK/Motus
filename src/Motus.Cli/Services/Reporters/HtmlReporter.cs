using System.Text;
using System.Web;
using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class HtmlReporter(string outputPath) : IReporter
{
    private readonly List<Abstractions.TestResult> _results = [];

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

    public Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        _results.Add(result);
        return Task.CompletedTask;
    }

    public async Task OnTestRunEndAsync(TestRunSummary summary)
    {
        var total = summary.Passed + summary.Failed + summary.Skipped;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"><title>Motus Test Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: system-ui, sans-serif; margin: 2rem; }");
        sb.AppendLine("table { border-collapse: collapse; width: 100%; }");
        sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
        sb.AppendLine("th { background: #f5f5f5; }");
        sb.AppendLine(".pass { color: #22863a; } .fail { color: #cb2431; }");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<h1>Motus Test Report</h1>");
        sb.AppendLine($"<p>{summary.Passed} passed, {summary.Failed} failed, {total} total ({summary.TotalDurationMs / 1000:F1}s)</p>");
        sb.AppendLine("<table><thead><tr><th>Status</th><th>Test</th><th>Duration</th><th>Error</th></tr></thead><tbody>");

        foreach (var r in _results)
        {
            var cls = r.Passed ? "pass" : "fail";
            var status = r.Passed ? "PASS" : "FAIL";
            var error = r.ErrorMessage is not null ? HttpUtility.HtmlEncode(r.ErrorMessage) : "";
            sb.AppendLine($"<tr><td class=\"{cls}\">{status}</td><td>{HttpUtility.HtmlEncode(r.TestName)}</td><td>{r.DurationMs:F0}ms</td><td>{error}</td></tr>");
        }

        sb.AppendLine("</tbody></table></body></html>");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outputPath, sb.ToString());
        Console.WriteLine($"HTML report written to {outputPath}");
    }
}
