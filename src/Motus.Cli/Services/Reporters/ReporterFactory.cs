using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public static class ReporterFactory
{
    public static IReporter Create(string[] specs)
    {
        var reporters = specs.Select(CreateSingle).ToList();
        return reporters.Count == 1 ? reporters[0] : new CompositeReporter(reporters);
    }

    private static IReporter CreateSingle(string spec)
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
            "trx" => new TrxReporter(path),
            _ => throw new ArgumentException($"Unknown reporter format: {format}"),
        };
    }
}
