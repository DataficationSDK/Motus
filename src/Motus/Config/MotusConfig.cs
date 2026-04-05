using System.Text.Json;

namespace Motus;

internal sealed record MotusFailureConfig(
    bool? Screenshot = null,
    string? ScreenshotPath = null,
    bool? Trace = null,
    string? TracePath = null);

internal sealed record MotusAssertionsConfig(
    int? Timeout = null);

internal sealed record MotusLaunchConfig(
    bool? Headless = null,
    string? Channel = null,
    int? SlowMo = null,
    int? Timeout = null);

internal sealed record MotusContextConfig(
    string? Locale = null,
    string? ColorScheme = null,
    bool? RecordVideo = null,
    bool? IgnoreHTTPSErrors = null,
    MotusViewportConfig? Viewport = null);

internal sealed record MotusViewportConfig(
    int? Width = null,
    int? Height = null);

internal sealed record MotusLocatorConfig(
    int? Timeout = null,
    string[]? SelectorPriority = null);

internal sealed record MotusReporterConfig(
    string[]? Default = null,
    string[]? Ci = null);

internal sealed record MotusRecorderConfig(
    string? Output = null,
    string? Framework = null,
    string[]? SelectorPriority = null);

internal sealed record MotusAccessibilityConfig(
    bool? Enable = null,
    string? Mode = null,
    bool? AuditAfterNavigation = null,
    bool? AuditAfterActions = null,
    bool? IncludeWarnings = null,
    string[]? SkipRules = null);

internal sealed record MotusRootConfig(
    string? Motus = null,
    MotusLaunchConfig? Launch = null,
    MotusContextConfig? Context = null,
    MotusLocatorConfig? Locator = null,
    MotusReporterConfig? Reporter = null,
    MotusAssertionsConfig? Assertions = null,
    MotusRecorderConfig? Recorder = null,
    MotusFailureConfig? Failure = null,
    MotusAccessibilityConfig? Accessibility = null);

internal static class MotusConfigLoader
{
    private static readonly Lazy<MotusRootConfig> _config = new(Load);

    internal static MotusRootConfig Config => _config.Value;

    internal static MotusRootConfig LoadFrom(string? json, Func<string, string?>? envReader = null)
    {
        var config = DeserializeJson(json);
        return ApplyEnvironmentVariables(config, envReader ?? Environment.GetEnvironmentVariable);
    }

    private static MotusRootConfig Load()
    {
        try
        {
            var json = LoadJsonFromFile();
            var config = DeserializeJson(json);
            return ApplyEnvironmentVariables(config, Environment.GetEnvironmentVariable);
        }
        catch
        {
            // Config loading must never fail; return defaults
            return new MotusRootConfig();
        }
    }

    private static string? LoadJsonFromFile()
    {
        var dir = Environment.CurrentDirectory;
        while (dir is not null)
        {
            var configPath = Path.Combine(dir, "motus.config.json");
            if (File.Exists(configPath))
                return File.ReadAllText(configPath);
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static MotusRootConfig DeserializeJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new MotusRootConfig();

        var config = JsonSerializer.Deserialize(json, MotusConfigJsonContext.Default.MotusRootConfig);
        return config ?? new MotusRootConfig();
    }

    private static MotusRootConfig ApplyEnvironmentVariables(MotusRootConfig config, Func<string, string?>? envReader)
    {
        if (envReader is null)
            return config;

        var launch = config.Launch ?? new MotusLaunchConfig();
        var launchChanged = false;

        if (TryParseBool(envReader("MOTUS_HEADLESS"), out var headless))
        { launch = launch with { Headless = headless }; launchChanged = true; }

        if (envReader("MOTUS_CHANNEL") is { Length: > 0 } channel)
        { launch = launch with { Channel = channel }; launchChanged = true; }

        if (TryParseInt(envReader("MOTUS_SLOWMO"), out var slowMo))
        { launch = launch with { SlowMo = slowMo }; launchChanged = true; }

        if (TryParseInt(envReader("MOTUS_LAUNCH_TIMEOUT"), out var launchTimeout))
        { launch = launch with { Timeout = launchTimeout }; launchChanged = true; }

        var context = config.Context ?? new MotusContextConfig();
        var contextChanged = false;

        if (envReader("MOTUS_LOCALE") is { Length: > 0 } locale)
        { context = context with { Locale = locale }; contextChanged = true; }

        if (envReader("MOTUS_COLOR_SCHEME") is { Length: > 0 } colorScheme)
        { context = context with { ColorScheme = colorScheme }; contextChanged = true; }

        if (TryParseBool(envReader("MOTUS_IGNORE_HTTPS_ERRORS"), out var ignoreHttps))
        { context = context with { IgnoreHTTPSErrors = ignoreHttps }; contextChanged = true; }

        var locator = config.Locator ?? new MotusLocatorConfig();
        var locatorChanged = false;

        if (TryParseInt(envReader("MOTUS_LOCATOR_TIMEOUT"), out var locatorTimeout))
        { locator = locator with { Timeout = locatorTimeout }; locatorChanged = true; }

        var assertions = config.Assertions ?? new MotusAssertionsConfig();
        var assertionsChanged = false;

        if (TryParseInt(envReader("MOTUS_ASSERTIONS_TIMEOUT"), out var assertionsTimeout))
        { assertions = assertions with { Timeout = assertionsTimeout }; assertionsChanged = true; }

        var failure = config.Failure ?? new MotusFailureConfig();
        var failureChanged = false;

        if (TryParseBool(envReader("MOTUS_FAILURES_SCREENSHOT"), out var screenshot))
        { failure = failure with { Screenshot = screenshot }; failureChanged = true; }

        if (envReader("MOTUS_FAILURES_SCREENSHOT_PATH") is { Length: > 0 } screenshotPath)
        { failure = failure with { ScreenshotPath = screenshotPath }; failureChanged = true; }

        if (TryParseBool(envReader("MOTUS_FAILURES_TRACE"), out var trace))
        { failure = failure with { Trace = trace }; failureChanged = true; }

        if (envReader("MOTUS_FAILURES_TRACE_PATH") is { Length: > 0 } tracePath)
        { failure = failure with { TracePath = tracePath }; failureChanged = true; }

        var accessibility = config.Accessibility ?? new MotusAccessibilityConfig();
        var accessibilityChanged = false;

        if (TryParseBool(envReader("MOTUS_ACCESSIBILITY_ENABLE"), out var a11yEnable))
        { accessibility = accessibility with { Enable = a11yEnable }; accessibilityChanged = true; }

        if (envReader("MOTUS_ACCESSIBILITY_MODE") is { Length: > 0 } a11yMode)
        { accessibility = accessibility with { Mode = a11yMode }; accessibilityChanged = true; }

        return config with
        {
            Launch = launchChanged ? launch : config.Launch,
            Context = contextChanged ? context : config.Context,
            Locator = locatorChanged ? locator : config.Locator,
            Assertions = assertionsChanged ? assertions : config.Assertions,
            Failure = failureChanged ? failure : config.Failure,
            Accessibility = accessibilityChanged ? accessibility : config.Accessibility,
        };
    }

    private static bool TryParseBool(string? value, out bool result)
    {
        if (value is not null)
        {
            var lower = value.Trim().ToLowerInvariant();
            if (lower is "true" or "1") { result = true; return true; }
            if (lower is "false" or "0") { result = false; return true; }
        }
        result = default;
        return false;
    }

    private static bool TryParseInt(string? value, out int result)
    {
        if (value is not null && int.TryParse(value.Trim(), out result))
            return true;
        result = default;
        return false;
    }
}
