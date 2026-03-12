using System.Text.Json;

namespace Motus;

internal sealed record MotusFailureConfig(
    bool Screenshot = false,
    string ScreenshotPath = "test-results/failures",
    bool Trace = false);

internal static class MotusConfigLoader
{
    private static readonly Lazy<MotusFailureConfig> _config = new(Load);

    internal static MotusFailureConfig Config => _config.Value;

    private static MotusFailureConfig Load()
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
                    var config = JsonSerializer.Deserialize(json, MotusConfigJsonContext.Default.MotusFailureConfig);
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

        return new MotusFailureConfig();
    }
}
