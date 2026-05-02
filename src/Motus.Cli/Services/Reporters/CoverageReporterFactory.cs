using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

/// <summary>
/// Parses <c>--coverage</c> specs into <see cref="ICoverageReporter"/> instances.
/// Supported specs: <c>console</c>, <c>html:&lt;dir&gt;</c>, <c>cobertura:&lt;path&gt;</c>.
/// An empty list yields a single console reporter (default behaviour for bare <c>--coverage</c>).
/// </summary>
public static class CoverageReporterFactory
{
    public static IReadOnlyList<ICoverageReporter> Create(IReadOnlyList<string>? specs)
    {
        if (specs is null || specs.Count == 0)
            return new ICoverageReporter[] { new CoverageConsoleReporter() };

        var result = new List<ICoverageReporter>(specs.Count);
        foreach (var spec in specs)
            result.Add(CreateSingle(spec));
        return result;
    }

    private const string SupportedFormats =
        "Supported formats: console | html:<dir> | cobertura:<path>";

    private static ICoverageReporter CreateSingle(string spec)
    {
        var colonIdx = spec.IndexOf(':');
        if (colonIdx < 0)
        {
            return spec.ToLowerInvariant() switch
            {
                "console" => new CoverageConsoleReporter(),
                "html" => throw new ArgumentException(
                    "Coverage format 'html' requires an output directory. " +
                    "Use --coverage html:<dir> (e.g. --coverage html:./coverage)."),
                "cobertura" => throw new ArgumentException(
                    "Coverage format 'cobertura' requires an output file path. " +
                    "Use --coverage cobertura:<path> (e.g. --coverage cobertura:./coverage.xml)."),
                _ => throw new ArgumentException(
                    $"Unknown coverage format '{spec}'. {SupportedFormats}."),
            };
        }

        var format = spec[..colonIdx].ToLowerInvariant();
        var path = spec[(colonIdx + 1)..];

        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException(
                $"Coverage format '{format}' was given an empty target. " +
                $"Use --coverage {format}:<{(format == "html" ? "dir" : "path")}>.");

        return format switch
        {
            "html" => new CoverageHtmlReporter(path),
            "cobertura" => new CoberturaReporter(path),
            "console" => throw new ArgumentException(
                "Coverage format 'console' does not take a target. Use --coverage console."),
            _ => throw new ArgumentException(
                $"Unknown coverage format '{format}'. {SupportedFormats}."),
        };
    }
}
