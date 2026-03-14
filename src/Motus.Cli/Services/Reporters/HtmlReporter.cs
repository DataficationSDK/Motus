using System.Text;
using System.Web;
using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class HtmlReporter(string outputPath) : IReporter
{
    private static readonly HashSet<string> ImageExtensions =
        new([".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg"], StringComparer.OrdinalIgnoreCase);

    private readonly List<(TestInfo Info, Abstractions.TestResult Result)> _results = [];

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

    public Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        _results.Add((test, result));
        return Task.CompletedTask;
    }

    public async Task OnTestRunEndAsync(TestRunSummary summary)
    {
        var total = summary.Passed + summary.Failed + summary.Skipped;
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"><title>Motus Test Report</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("""
body { font-family: system-ui, sans-serif; margin: 0; padding: 2rem; background: #fafafa; color: #24292e; }
h1 { margin-top: 0; }
.summary { display: flex; gap: 1rem; margin-bottom: 1.5rem; }
.summary .stat { padding: 0.75rem 1.25rem; border-radius: 6px; font-weight: 600; }
.stat.passed { background: #dcffe4; color: #22863a; }
.stat.failed { background: #ffeef0; color: #cb2431; }
.stat.skipped { background: #fff8c5; color: #735c0f; }
.stat.total { background: #f1f8ff; color: #0366d6; }
.test-list { list-style: none; padding: 0; }
.test-item { border: 1px solid #e1e4e8; border-radius: 6px; margin-bottom: 0.5rem; background: #fff; }
.test-item details { padding: 0; }
.test-item summary { padding: 0.75rem 1rem; cursor: pointer; display: flex; align-items: center; gap: 0.75rem; }
.test-item summary:hover { background: #f6f8fa; }
.badge { display: inline-block; padding: 2px 8px; border-radius: 12px; font-size: 0.75rem; font-weight: 600; text-transform: uppercase; }
.badge.pass { background: #dcffe4; color: #22863a; }
.badge.fail { background: #ffeef0; color: #cb2431; }
.badge.skip { background: #fff8c5; color: #735c0f; }
.test-name { flex: 1; }
.duration { color: #6a737d; font-size: 0.875rem; }
.detail-body { padding: 0.75rem 1rem; border-top: 1px solid #e1e4e8; }
.error-msg { color: #cb2431; margin: 0.5rem 0; }
pre.stack-trace { background: #f6f8fa; padding: 1rem; border-radius: 4px; overflow-x: auto; font-size: 0.8rem; margin: 0.5rem 0; }
.attachments { margin-top: 0.75rem; }
.attachments h4 { margin: 0 0 0.5rem; font-size: 0.875rem; }
.attachments img { max-width: 100%; border: 1px solid #e1e4e8; border-radius: 4px; margin: 0.25rem 0; }
.attachments a { display: block; color: #0366d6; margin: 0.25rem 0; }
""");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>Motus Test Report</h1>");

        // Summary bar
        sb.AppendLine("<div class=\"summary\">");
        sb.AppendLine($"<div class=\"stat passed\">{summary.Passed} Passed</div>");
        sb.AppendLine($"<div class=\"stat failed\">{summary.Failed} Failed</div>");
        if (summary.Skipped > 0)
            sb.AppendLine($"<div class=\"stat skipped\">{summary.Skipped} Skipped</div>");
        sb.AppendLine($"<div class=\"stat total\">{total} Total ({summary.TotalDurationMs / 1000:F1}s)</div>");
        sb.AppendLine("</div>");

        // Test list
        sb.AppendLine("<ul class=\"test-list\">");
        foreach (var (info, r) in _results)
        {
            var badgeClass = r.Passed ? "pass" : "fail";
            var badgeText = r.Passed ? "PASS" : "FAIL";
            var hasDetails = !r.Passed || r.Attachments is { Count: > 0 };

            sb.AppendLine("<li class=\"test-item\">");

            if (hasDetails)
            {
                sb.AppendLine("<details>");
                sb.AppendLine($"<summary><span class=\"badge {badgeClass}\">{badgeText}</span><span class=\"test-name\">{HttpUtility.HtmlEncode(r.TestName)}</span><span class=\"duration\">{r.DurationMs:F0}ms</span></summary>");
                sb.AppendLine("<div class=\"detail-body\">");

                if (!r.Passed && r.ErrorMessage is not null)
                    sb.AppendLine($"<p class=\"error-msg\">{HttpUtility.HtmlEncode(r.ErrorMessage)}</p>");

                if (!r.Passed && r.StackTrace is not null)
                    sb.AppendLine($"<pre class=\"stack-trace\">{HttpUtility.HtmlEncode(r.StackTrace)}</pre>");

                if (r.Attachments is { Count: > 0 })
                {
                    sb.AppendLine("<div class=\"attachments\"><h4>Attachments</h4>");
                    foreach (var attachment in r.Attachments)
                    {
                        var ext = Path.GetExtension(attachment);
                        if (ImageExtensions.Contains(ext) && File.Exists(attachment))
                        {
                            var bytes = File.ReadAllBytes(attachment);
                            var base64 = Convert.ToBase64String(bytes);
                            var mime = ext.ToLowerInvariant() switch
                            {
                                ".svg" => "image/svg+xml",
                                ".webp" => "image/webp",
                                ".gif" => "image/gif",
                                ".bmp" => "image/bmp",
                                _ => "image/png",
                            };
                            sb.AppendLine($"<img src=\"data:{mime};base64,{base64}\" alt=\"{HttpUtility.HtmlEncode(Path.GetFileName(attachment))}\" />");
                        }
                        else
                        {
                            sb.AppendLine($"<a href=\"{HttpUtility.HtmlEncode(attachment)}\">{HttpUtility.HtmlEncode(Path.GetFileName(attachment))}</a>");
                        }
                    }
                    sb.AppendLine("</div>");
                }

                sb.AppendLine("</div></details>");
            }
            else
            {
                // No details needed for passing tests without attachments
                sb.AppendLine($"<div style=\"padding: 0.75rem 1rem; display: flex; align-items: center; gap: 0.75rem;\"><span class=\"badge {badgeClass}\">{badgeText}</span><span class=\"test-name\">{HttpUtility.HtmlEncode(r.TestName)}</span><span class=\"duration\">{r.DurationMs:F0}ms</span></div>");
            }

            sb.AppendLine("</li>");
        }
        sb.AppendLine("</ul>");
        sb.AppendLine("</body></html>");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outputPath, sb.ToString());
        Console.WriteLine($"HTML report written to {outputPath}");
    }
}
