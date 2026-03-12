using System.Text.Json;

namespace Motus;

internal sealed record MotusFailureConfig(
    bool Screenshot = false,
    string ScreenshotPath = "test-results/failures",
    bool Trace = false);

internal sealed record MotusAssertionsConfig(
    int Timeout = 30_000);

internal sealed record MotusRootConfig(
    MotusFailureConfig? Failure = null,
    MotusAssertionsConfig? Assertions = null);

internal static class MotusConfigLoader
{
    private static readonly Lazy<MotusRootConfig> _config = new(Load);

    internal static MotusRootConfig Config => _config.Value;

    private static MotusRootConfig Load()
    {
        try
        {
            var dir = Environment.CurrentDirectory;
            while (dir is not null)
            {
                var configPath = Path.Combine(dir, "motus.config.json");
                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    var config = JsonSerializer.Deserialize(json, MotusConfigJsonContext.Default.MotusRootConfig);
                    if (config is not null)
                        return config;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch
        {
            // Config loading must never fail; return defaults
        }

        return new MotusRootConfig();
    }
}
