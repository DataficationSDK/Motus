using System.Text.Json;
using System.Text.Json.Serialization;
using Motus.Abstractions;

namespace Motus.Cli.Services;

/// <summary>
/// One test's accumulated history across runs. <see cref="Runs"/> counts every recorded
/// execution; <see cref="Failures"/> counts runs that ended failed; <see cref="FlakyPasses"/>
/// counts runs that passed only after a retry. A flake rate can be derived as
/// (Failures + FlakyPasses) / Runs.
/// </summary>
public sealed record FlakeRecord(int Runs, int Failures, int FlakyPasses, string LastSeenUtc);

[JsonSerializable(typeof(Dictionary<string, FlakeRecord>), TypeInfoPropertyName = "FlakeHistory")]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class FlakeHistoryJsonContext : JsonSerializerContext;

/// <summary>
/// A reporter that accumulates per-test run/failure/flaky-pass counts into a JSON file,
/// merging with any existing history so a flake rate can be tracked over time. Consumes the
/// normal test lifecycle events, so it composes alongside any other reporters.
/// </summary>
public sealed class FlakeHistoryReporter(string outputPath) : IReporter
{
    private readonly Dictionary<string, (int Runs, int Failures, int FlakyPasses)> _thisRun = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

    public Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        lock (_lock)
        {
            _thisRun.TryGetValue(result.TestName, out var acc);
            acc.Runs += 1;
            if (!result.Passed)
                acc.Failures += 1;
            if (result.Flaky)
                acc.FlakyPasses += 1;
            _thisRun[result.TestName] = acc;
        }
        return Task.CompletedTask;
    }

    public async Task OnTestRunEndAsync(TestRunSummary summary)
    {
        var merged = await ReadExistingAsync();
        var nowUtc = DateTime.UtcNow.ToString("o");

        lock (_lock)
        {
            foreach (var (name, acc) in _thisRun)
            {
                merged.TryGetValue(name, out var prev);
                merged[name] = new FlakeRecord(
                    (prev?.Runs ?? 0) + acc.Runs,
                    (prev?.Failures ?? 0) + acc.Failures,
                    (prev?.FlakyPasses ?? 0) + acc.FlakyPasses,
                    nowUtc);
            }
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(merged, FlakeHistoryJsonContext.Default.FlakeHistory);
        await File.WriteAllTextAsync(outputPath, json);
        Console.WriteLine($"Flake history written to {outputPath}");
    }

    private async Task<Dictionary<string, FlakeRecord>> ReadExistingAsync()
    {
        try
        {
            if (!File.Exists(outputPath))
                return new Dictionary<string, FlakeRecord>(StringComparer.Ordinal);

            var json = await File.ReadAllTextAsync(outputPath);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, FlakeRecord>(StringComparer.Ordinal);

            var existing = JsonSerializer.Deserialize(json, FlakeHistoryJsonContext.Default.FlakeHistory);
            return existing is null
                ? new Dictionary<string, FlakeRecord>(StringComparer.Ordinal)
                : new Dictionary<string, FlakeRecord>(existing, StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable history file must not fail the run; start fresh.
            Console.Error.WriteLine($"Warning: could not read flake history '{outputPath}': {ex.Message}");
            return new Dictionary<string, FlakeRecord>(StringComparer.Ordinal);
        }
    }
}
