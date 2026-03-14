using System.Diagnostics;
using System.Runtime.InteropServices;
using Motus.Runner.Services;

namespace Motus.Runner;

public static class RunnerHost
{
    public static async Task StartAsync(
        string[] args,
        string[]? assemblyPaths = null,
        string? filter = null,
        int port = 5100,
        CancellationToken ct = default)
    {
        var runnerAssembly = typeof(RunnerHost).Assembly;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = runnerAssembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.WebHost.UseStaticWebAssets();

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

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var app = builder.Build();

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

        var url = $"http://localhost:{port}";
        Console.WriteLine($"Motus Runner started at {url}");
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
