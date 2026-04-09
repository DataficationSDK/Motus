using Motus.Abstractions;

namespace Motus;

internal static class ConfigMerge
{
    internal static LaunchOptions ApplyConfig(LaunchOptions options)
    {
        var result = options;

        var launch = MotusConfigLoader.Config.Launch;
        if (launch is not null)
        {
            if (options.Headless && launch.Headless.HasValue)
                result = result with { Headless = launch.Headless.Value };

            if (options.Channel is null && launch.Channel is not null
                && Enum.TryParse<BrowserChannel>(launch.Channel, ignoreCase: true, out var channel))
                result = result with { Channel = channel };

            if (options.SlowMo == 0 && launch.SlowMo.HasValue)
                result = result with { SlowMo = launch.SlowMo.Value };

            if (options.Timeout == 30_000 && launch.Timeout.HasValue)
                result = result with { Timeout = launch.Timeout.Value };
        }

        var a11y = MotusConfigLoader.Config.Accessibility;
        if (a11y is not null && options.Accessibility is null)
        {
            var mode = AccessibilityMode.Enforce;
            if (a11y.Mode is not null)
                Enum.TryParse(a11y.Mode, ignoreCase: true, out mode);

            result = result with
            {
                Accessibility = new AccessibilityOptions
                {
                    Enable = a11y.Enable ?? false,
                    Mode = mode,
                    AuditAfterNavigation = a11y.AuditAfterNavigation ?? true,
                    AuditAfterActions = a11y.AuditAfterActions ?? false,
                    IncludeWarnings = a11y.IncludeWarnings ?? true,
                    SkipRules = a11y.SkipRules,
                }
            };
        }

        var perf = MotusConfigLoader.Config.Performance;
        if (perf is not null && options.Performance is null)
        {
            result = result with
            {
                Performance = new PerformanceOptions
                {
                    Enable = perf.Enable ?? false,
                    CollectAfterNavigation = perf.CollectAfterNavigation ?? true,
                }
            };
        }

        return result;
    }

    internal static PerformanceBudget? ToBudget(MotusPerformanceConfig? cfg)
    {
        if (cfg is null)
            return null;

        if (cfg.Lcp is null && cfg.Fcp is null && cfg.Ttfb is null &&
            cfg.Cls is null && cfg.Inp is null && cfg.JsHeapSize is null &&
            cfg.DomNodeCount is null)
            return null;

        return new PerformanceBudget
        {
            Lcp = cfg.Lcp,
            Fcp = cfg.Fcp,
            Ttfb = cfg.Ttfb,
            Cls = cfg.Cls,
            Inp = cfg.Inp,
            JsHeapSize = cfg.JsHeapSize,
            DomNodeCount = cfg.DomNodeCount,
        };
    }

    internal static ContextOptions ApplyConfig(ContextOptions options)
    {
        var context = MotusConfigLoader.Config.Context;
        if (context is null)
            return options;

        var result = options;

        if (options.Locale is null && context.Locale is not null)
            result = result with { Locale = context.Locale };

        if (options.ColorScheme is null && context.ColorScheme is not null
            && Enum.TryParse<ColorScheme>(context.ColorScheme, ignoreCase: true, out var scheme))
            result = result with { ColorScheme = scheme };

        if (!options.IgnoreHTTPSErrors && context.IgnoreHTTPSErrors is true)
            result = result with { IgnoreHTTPSErrors = true };

        if (options.Viewport is null && context.Viewport is { Width: not null, Height: not null })
            result = result with { Viewport = new ViewportSize(context.Viewport.Width.Value, context.Viewport.Height.Value) };

        return result;
    }
}
