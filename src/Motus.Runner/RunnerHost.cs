using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.FileProviders;
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
        // Resolve the directory containing Motus.Runner.dll. When running from
        // a build output this is bin/Debug|Release; when installed as a global
        // tool it is the tool store directory.
        var runnerAssemblyDir = Path.GetDirectoryName(
            typeof(RunnerHost).Assembly.Location)!;

        // Determine whether the static web assets manifest has valid paths.
        // In Development mode ASP.NET Core uses the manifest to locate wwwroot
        // content and _framework/ files. The manifest contains absolute paths
        // from the machine that built the package (typically CI), so it only
        // works when running from the original build output. When installed as
        // a global tool we fall back to Production mode and serve the bundled
        // wwwroot via a PhysicalFileProvider instead.
        var useDevMode = HasValidStaticWebAssetsManifest(runnerAssemblyDir);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = "Motus.Runner",
            ContentRootPath = runnerAssemblyDir,
            EnvironmentName = useDevMode ? "Development" : "Production",
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
            TraceMode = traceFilePath is not null,
            TraceFilePath = traceFilePath,
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

            // In Production mode (global tool install), serve the bundled
            // wwwroot files that were packed alongside the assembly.
            var wwwrootPath = Path.Combine(runnerAssemblyDir, "wwwroot");
            if (Directory.Exists(wwwrootPath))
            {
                app.UseStaticFiles(new StaticFileOptions
                {
                    FileProvider = new PhysicalFileProvider(wwwrootPath),
                });
            }
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

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            Console.WriteLine($"Motus Runner started at {url}");
            Console.WriteLine("Press Ctrl+C to stop.");
            OpenBrowser(url);
        });

        await app.RunAsync(ct);
    }

    /// <summary>
    /// Returns true when the static web assets manifest exists and its ContentRoots
    /// point to directories that exist on disk (i.e. running from the original build
    /// output). Returns false when installed as a global tool where the manifest
    /// contains absolute paths from the CI build machine.
    /// </summary>
    private static bool HasValidStaticWebAssetsManifest(string assemblyDir)
    {
        var manifestPath = Path.Combine(assemblyDir, "Motus.Runner.staticwebassets.runtime.json");
        if (!File.Exists(manifestPath))
            return false;

        try
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifestPath));
            var contentRoots = json?["ContentRoots"]?.AsArray();
            if (contentRoots is null || contentRoots.Count == 0)
                return false;

            var firstRoot = contentRoots[0]?.GetValue<string>();
            return firstRoot is not null && Directory.Exists(firstRoot);
        }
        catch
        {
            return false;
        }
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
