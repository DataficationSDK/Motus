using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using Motus.Runner.Services.Timeline;

namespace Motus.Runner.Services.Execution;

/// <summary>What a runner wants from a test host besides results.</summary>
internal sealed class TestHostOptions
{
    /// <summary>Stream the browser and record a timeline, for the visual runner.</summary>
    public bool Visual { get; init; }

    /// <summary>Told each warning the test host reports, such as a cleanup method that threw.</summary>
    public Action<string>? OnLog { get; init; }

    /// <summary>Told each screencast frame, as base64 JPEG.</summary>
    public Action<string>? OnFrame { get; init; }

    /// <summary>Told each timeline entry the recorder hook makes.</summary>
    public Action<TimelineEntry>? OnTimelineEntry { get; init; }

    /// <summary>
    /// Asked before each browser action when <see cref="Visual"/> is set; the action waits for the
    /// returned task, which is how step debugging holds it.
    /// </summary>
    public Func<string, string?, Task>? OnStep { get; init; }

    /// <summary>How long the test host may take to start and connect.</summary>
    public TimeSpan StartTimeout { get; init; } = TimeSpan.FromSeconds(60);
}

/// <summary>The tests a test host found in its assembly.</summary>
internal sealed record TestHostDiscovery(string AssemblyName, IReadOnlyList<TestCaseInfo> Tests);

/// <summary>A test host that could not start, could not load its assembly, or exited.</summary>
internal sealed class TestHostException(string message) : Exception(message);

/// <summary>
/// The runner's side of a test host process (<see cref="TestHostServer"/>): starts it for one test
/// assembly, then discovers and runs that assembly's tests in it. Several runs may be in flight
/// at once. When the process exits unexpectedly, the runs in flight and every later one fail with
/// a message that says so, and its own output, including any crash report, has already gone to
/// this process's console, which it shares.
/// </summary>
internal sealed class TestHostClient : ITestAssemblyHost
{
    private readonly Process _process;
    private readonly TestHostConnection _connection;
    private readonly TestHostOptions _options;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<TestHostMessage>> _pending = new();
    private readonly Task _readLoop;
    private readonly object _initializeLock = new();
    private Task<TestHostFailure?>? _initialize;
    private volatile string? _exitMessage;
    private volatile bool _disposing;
    private int _nextId;

    private TestHostClient(string assemblyPath, Process process, NamedPipeServerStream pipe, TestHostOptions options)
    {
        AssemblyPath = assemblyPath;
        _process = process;
        _options = options;
        _connection = new TestHostConnection(pipe);
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>The full path of the test assembly this host runs.</summary>
    public string AssemblyPath { get; }

    private string FileName => Path.GetFileName(AssemblyPath);

    /// <summary>Starts a test host for <paramref name="assemblyPath"/> and waits for it to connect.</summary>
    /// <exception cref="TestHostException">The process could not be started or exited first.</exception>
    public static async Task<TestHostClient> StartAsync(string assemblyPath, TestHostOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new TestHostOptions();
        assemblyPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(assemblyPath))
            throw new TestHostException("The file does not exist.");

        var pipeName = TestHostLauncher.NewPipeName();
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        Process process;
        try
        {
            process = Process.Start(TestHostLauncher.CreateStartInfo(pipeName, assemblyPath, options.Visual))
                ?? throw new TestHostException("Could not start a test host process.");
        }
        catch (Exception ex) when (ex is not TestHostException)
        {
            await pipe.DisposeAsync();
            throw new TestHostException($"Could not start a test host process: {ex.Message}");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(options.StartTimeout);
        var connected = pipe.WaitForConnectionAsync(timeout.Token);
        var exited = process.WaitForExitAsync(timeout.Token);
        try
        {
            await Task.WhenAny(connected, exited);
        }
        catch
        {
            // Inspected below.
        }

        if (connected.IsCompletedSuccessfully)
            return new TestHostClient(assemblyPath, process, pipe, options);

        var message = process.HasExited
            ? $"The test host exited with code {process.ExitCode} before it started. Its output above says why."
            : $"The test host did not start within {options.StartTimeout.TotalSeconds:0} seconds.";
        Kill(process);
        process.Dispose();
        await pipe.DisposeAsync();
        cancellationToken.ThrowIfCancellationRequested();
        throw new TestHostException(message);
    }

    /// <summary>Discovers the assembly's tests whose full name contains <paramref name="filter"/>.</summary>
    /// <exception cref="TestHostException">The assembly could not be loaded, or the host exited.</exception>
    public async Task<TestHostDiscovery> DiscoverAsync(string? filter, CancellationToken cancellationToken = default)
    {
        var reply = await RequestAsync(new TestHostMessage { Type = TestHostMessageTypes.Discover, Name = filter }, cancellationToken);
        if (reply.Failure is { } failure)
            throw new TestHostException(failure.Message);

        return new TestHostDiscovery(
            reply.AssemblyName ?? Path.GetFileNameWithoutExtension(AssemblyPath),
            reply.Tests ?? []);
    }

    public Task<TestHostFailure?> InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initializeLock)
            return _initialize ??= InitializeOnceAsync();
    }

    private async Task<TestHostFailure?> InitializeOnceAsync()
    {
        try
        {
            var reply = await RequestAsync(new TestHostMessage { Type = TestHostMessageTypes.Initialize }, CancellationToken.None);
            return reply.Failure;
        }
        catch (TestHostException ex)
        {
            return new TestHostFailure(ex.Message, null);
        }
    }

    public async Task<TestCaseReport> RunAsync(string fullName, CancellationToken cancellationToken = default)
    {
        if (_exitMessage is { } exited)
            return TestCaseReport.NotRun(fullName, $"{exited} The test did not run.");

        var id = Interlocked.Increment(ref _nextId);

        // Stopping a run cancels the test's TestContext token in the host; the test runs on to its end.
        using var cancel = cancellationToken.Register(() =>
            _connection.Post(new TestHostMessage { Type = TestHostMessageTypes.Cancel, Id = id }));

        try
        {
            var reply = await RequestAsync(new TestHostMessage { Type = TestHostMessageTypes.Run, Id = id, Name = fullName }, CancellationToken.None);
            return reply.Report ?? TestCaseReport.NotRun(fullName, "The test host sent no result.");
        }
        catch (TestHostException ex)
        {
            return TestCaseReport.NotRun(fullName, $"{ex.Message} The test was running at the time.");
        }
    }

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        if (_exitMessage is not null)
            return;

        try
        {
            await RequestAsync(new TestHostMessage { Type = TestHostMessageTypes.Cleanup }, cancellationToken);
        }
        catch (TestHostException)
        {
            // The host exited; the tests it was running have already said so.
        }
    }

    /// <summary>
    /// Closes the pipe, which ends the test host, and waits briefly for it to exit before ending it
    /// and the processes it started, such as a browser a test left open.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _disposing = true;
        await _connection.DisposeAsync();

        try
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
            Kill(_process);
        }

        try { await _readLoop; }
        catch { /* the loop reports through _exitMessage */ }

        _process.Dispose();
    }

    private async Task<TestHostMessage> RequestAsync(TestHostMessage request, CancellationToken cancellationToken)
    {
        if (request.Id == 0)
            request.Id = Interlocked.Increment(ref _nextId);

        var reply = new TaskCompletionSource<TestHostMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.Id] = reply;

        // The read loop sets the exit message before it fails what is pending, so a request added
        // after it has looked is caught here.
        if (_exitMessage is { } exited)
        {
            _pending.TryRemove(request.Id, out _);
            throw new TestHostException(exited);
        }

        _connection.Post(request);
        return await reply.Task.WaitAsync(cancellationToken);
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (await _connection.ReadAsync() is { } message)
                Dispatch(message);
        }
        catch (Exception ex) when (!_disposing)
        {
            _options.OnLog?.Invoke($"Lost the connection to the test host for {FileName}: {ex.Message}");
        }

        _exitMessage = _disposing
            ? $"The test host for {FileName} has been shut down."
            : await DescribeExitAsync();

        foreach (var id in _pending.Keys)
        {
            if (_pending.TryRemove(id, out var pending))
                pending.TrySetException(new TestHostException(_exitMessage));
        }
    }

    private async Task<string> DescribeExitAsync()
    {
        try
        {
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            return $"The test host for {FileName} exited unexpectedly with code {_process.ExitCode}.";
        }
        catch (TimeoutException)
        {
            return $"The test host for {FileName} closed its connection unexpectedly.";
        }
    }

    private void Dispatch(TestHostMessage message)
    {
        try
        {
            switch (message.Type)
            {
                case TestHostMessageTypes.Log:
                    if (message.Text is not null)
                        _options.OnLog?.Invoke(message.Text);
                    break;

                case TestHostMessageTypes.Frame:
                    if (message.Text is not null)
                        _options.OnFrame?.Invoke(message.Text);
                    break;

                case TestHostMessageTypes.Timeline:
                    if (message.Entry is not null)
                        _options.OnTimelineEntry?.Invoke(message.Entry);
                    break;

                case TestHostMessageTypes.Step:
                    _ = ContinueStepAsync(message);
                    break;

                default:
                    if (_pending.TryRemove(message.Id, out var pending))
                        pending.TrySetResult(message);
                    break;
            }
        }
        catch (Exception ex)
        {
            _options.OnLog?.Invoke($"Could not handle a {message.Type} message from the test host for {FileName}: {ex.Message}");
        }
    }

    private async Task ContinueStepAsync(TestHostMessage step)
    {
        try
        {
            if (_options.OnStep is { } onStep)
                await onStep(step.Text ?? "", step.Selector);
        }
        catch
        {
            // A step the runner could not hold still goes ahead.
        }

        _connection.Post(new TestHostMessage { Type = TestHostMessageTypes.StepContinue, Id = step.Id });
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Already gone.
        }
    }
}

/// <summary>Builds the command line that starts a test host.</summary>
internal static class TestHostLauncher
{
    /// <summary>
    /// A pipe name short enough for the socket path it becomes on Linux and macOS, which is the
    /// temporary directory plus a prefix and is limited to about a hundred characters.
    /// </summary>
    public static string NewPipeName() => $"motus-{Guid.NewGuid():N}"[..22];

    /// <summary>
    /// <c>dotnet exec</c> of <c>Motus.Runner.dll</c> with the test assembly's own
    /// <c>.runtimeconfig.json</c> and <c>.deps.json</c>, so that the test resolves its
    /// dependencies, frameworks and base directory as it does under <c>dotnet test</c>.
    /// Standard output and error are the runner's own. The process starts in the runner's current
    /// directory and moves to the test assembly's folder once it has read its settings.
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string pipeName, string assemblyPath, bool visual)
    {
        var hostPath = typeof(TestHostServer).Assembly.Location;
        var stem = Path.Combine(Path.GetDirectoryName(assemblyPath)!, Path.GetFileNameWithoutExtension(assemblyPath));
        var runtimeConfig = stem + ".runtimeconfig.json";
        var deps = stem + ".deps.json";

        var startInfo = new ProcessStartInfo(FindDotnet())
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        startInfo.ArgumentList.Add("exec");

        // A class library built without a runtimeconfig runs on the runner's.
        if (!File.Exists(runtimeConfig))
            runtimeConfig = Path.ChangeExtension(hostPath, ".runtimeconfig.json");
        if (File.Exists(runtimeConfig))
        {
            startInfo.ArgumentList.Add("--runtimeconfig");
            startInfo.ArgumentList.Add(runtimeConfig);
        }

        if (File.Exists(deps))
        {
            startInfo.ArgumentList.Add("--depsfile");
            startInfo.ArgumentList.Add(deps);
        }

        // Motus.Runner.dll was built for one .NET version and cannot load on an older one. A test
        // built for an older one runs on the newest installed instead, as it did inside the runner.
        if (NeedsNewerRuntime(runtimeConfig))
        {
            startInfo.ArgumentList.Add("--roll-forward");
            startInfo.ArgumentList.Add("LatestMajor");
        }

        startInfo.ArgumentList.Add(hostPath);
        startInfo.ArgumentList.Add(TestHostServer.Command);
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add(assemblyPath);
        if (visual)
            startInfo.ArgumentList.Add(TestHostServer.VisualOption);

        return startInfo;
    }

    /// <summary>
    /// True when the runtimeconfig targets an older .NET major version than <c>Motus.Runner.dll</c>
    /// was built for, or does not say which it targets.
    /// </summary>
    internal static bool NeedsNewerRuntime(string runtimeConfigPath)
    {
        var hostMajor = MajorVersionOf(typeof(TestHostServer).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName);
        if (hostMajor is null)
            return false;

        try
        {
            var tfm = JsonNode.Parse(File.ReadAllText(runtimeConfigPath))?["runtimeOptions"]?["tfm"]?.GetValue<string>();
            return MajorVersionOfTfm(tfm) is not { } testMajor || testMajor < hostMajor;
        }
        catch
        {
            return true;
        }
    }

    private static int? MajorVersionOfTfm(string? tfm)
    {
        // "net8.0", "net10.0-windows"
        if (tfm is null || !tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase))
            return null;

        var version = tfm[3..];
        var platform = version.IndexOf('-');
        if (platform >= 0)
            version = version[..platform];
        return Version.TryParse(version, out var parsed) ? parsed.Major : null;
    }

    private static int? MajorVersionOf(string? frameworkName)
    {
        // ".NETCoreApp,Version=v10.0"
        var marker = frameworkName?.IndexOf("Version=v", StringComparison.Ordinal) ?? -1;
        if (marker < 0)
            return null;

        var version = frameworkName![(marker + "Version=v".Length)..];
        return Version.TryParse(version, out var parsed) ? parsed.Major : null;
    }

    /// <summary>
    /// The <c>dotnet</c> executable that runs this process, found from the runtime it loaded,
    /// since a global tool runs from its own launcher rather than from <c>dotnet</c>.
    /// </summary>
    internal static string FindDotnet()
    {
        var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

        if (Environment.ProcessPath is { } processPath
            && string.Equals(Path.GetFileName(processPath), executable, StringComparison.OrdinalIgnoreCase))
        {
            return processPath;
        }

        // <root>/shared/Microsoft.NETCore.App/<version>/System.Private.CoreLib.dll
        var runtimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            var root = Path.GetFullPath(Path.Combine(runtimeDirectory, "..", "..", ".."));
            var candidate = Path.Combine(root, executable);
            if (File.Exists(candidate))
                return candidate;
        }

        foreach (var variable in (string[])["DOTNET_HOST_PATH", "DOTNET_ROOT"])
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(value))
                continue;

            var candidate = File.Exists(value) && !Directory.Exists(value) ? value : Path.Combine(value, executable);
            if (File.Exists(candidate))
                return candidate;
        }

        return executable;
    }
}
