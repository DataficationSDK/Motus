using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Motus.Runner.Services;
using Motus.Runner.Services.Timeline;
using Motus.Runner.Services.VisualRegression;

namespace Motus.Runner;

public static class RunnerHost
{
    public static async Task StartAsync(
        string[] args,
        string[]? assemblyPaths = null,
        string? filter = null,
        int port = 5100,
        string? traceFilePath = null,
        bool verbose = false,
        CancellationToken ct = default)
    {
        // The static web assets manifest is named after the ApplicationName.
        // When hosted by Motus.Cli, the entry assembly is Motus.Cli but the
        // manifest ships as Motus.Runner.staticwebassets.runtime.json. Setting
        // ApplicationName to "Motus.Runner" and ContentRootPath to the Runner
        // assembly directory ensures the middleware discovers the correct manifest.
        var runnerAssemblyDir = Path.GetDirectoryName(
            typeof(RunnerHost).Assembly.Location)!;

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = "Motus.Runner",
            ContentRootPath = runnerAssemblyDir,
            EnvironmentName = "Development",
        });

        // Suppress ASP.NET Core info/warn noise unless verbose is set.
        // Errors still surface so startup failures are visible.
        builder.Logging.SetMinimumLevel(
            verbose ? LogLevel.Information : LogLevel.Error);

        builder.WebHost.UseUrls($"http://localhost:{port}");

        var options = new RunnerOptions
        {
            AssemblyPaths = assemblyPaths ?? [],
            Filter = filter,
            Port = port,
        };

        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<TestDiscovery>();
        builder.Services.AddSingleton<TestExecutionService>();
        builder.Services.AddSingleton<TestSessionService>();
        builder.Services.AddSingleton<ITestSessionService>(sp => sp.GetRequiredService<TestSessionService>());
        builder.Services.AddSingleton<ScreencastService>();
        builder.Services.AddSingleton<IScreencastService>(sp => sp.GetRequiredService<ScreencastService>());
        builder.Services.AddSingleton<TimelineService>();
        builder.Services.AddSingleton<ITimelineService>(sp => sp.GetRequiredService<TimelineService>());
        builder.Services.AddSingleton<StepDebugService>();
        builder.Services.AddSingleton<IStepDebugService>(sp => sp.GetRequiredService<StepDebugService>());
        builder.Services.AddSingleton<VisualRegressionService>();
        builder.Services.AddSingleton<IVisualRegressionService>(sp => sp.GetRequiredService<VisualRegressionService>());

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var app = builder.Build();

        var screencast = app.Services.GetRequiredService<IScreencastService>();
        var timeline = app.Services.GetRequiredService<ITimelineService>();
        var stepDebug = app.Services.GetRequiredService<IStepDebugService>();

        // Bridge explicit SetActivePage calls (e.g. from CLI commands)
        RunnerPageBridge.PageActivated += page =>
        {
            _ = Task.Run(async () =>
            {
                try { await screencast.AttachPageAsync(page); }
                catch { /* best-effort */ }
            });

            if (page is not null)
            {
                var hook = new TimelineRecorderHook(timeline, stepDebug);
                page.Context.GetPluginContext().RegisterLifecycleHook(hook);
            }
        };

        // Auto-detect pages created by tests via the global BrowserContext hook
        Motus.BrowserContext.GlobalPageCreated = page =>
        {
            _ = Task.Run(async () =>
            {
                try { await screencast.AttachPageAsync(page); }
                catch { /* best-effort */ }
            });

            var hook = new TimelineRecorderHook(timeline, stepDebug);
            page.Context.GetPluginContext().RegisterLifecycleHook(hook);
        };

        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<Motus.Runner.Components.App>()
            .AddInteractiveServerRenderMode();

        if (assemblyPaths is { Length: > 0 })
        {
            var session = app.Services.GetRequiredService<ITestSessionService>();
            await session.LoadAssembliesAsync(assemblyPaths, filter);
        }

        if (traceFilePath is not null)
        {
            var traceViewer = new TraceViewerService(timeline);
            await traceViewer.LoadFromFileAsync(traceFilePath);
        }

        var url = $"http://localhost:{port}";
        Console.WriteLine($"Motus Runner started at {url}");
        Console.WriteLine("Press Ctrl+C to stop.");
        OpenBrowser(url);

        await app.RunAsync(ct);
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Process.Start("open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                Process.Start("xdg-open", url);
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Browser launch is best-effort
        }
    }
}
