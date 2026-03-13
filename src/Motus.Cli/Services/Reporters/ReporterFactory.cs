namespace Motus.Cli.Services.Reporters;

public static class ReporterFactory
{
    public static ITestReporter Create(string spec)
    {
        var colonIdx = spec.IndexOf(':');
        if (colonIdx < 0)
        {
            return spec.ToLowerInvariant() switch
            {
                "console" => new ConsoleReporter(),
                _ => throw new ArgumentException($"Unknown reporter format: {spec}"),
            };
        }

        var format = spec[..colonIdx].ToLowerInvariant();
        var path = spec[(colonIdx + 1)..];

        return format switch
        {
            "junit" => new JUnitReporter(path),
            "html" => new HtmlReporter(path),
            _ => throw new ArgumentException($"Unknown reporter format: {format}"),
        };
    }
}
