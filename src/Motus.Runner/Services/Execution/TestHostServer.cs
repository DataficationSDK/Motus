using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Motus.Abstractions;
using Motus.Runner.Services.Timeline;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// The test host: a process of its own for one test assembly, started by <see cref="TestHostClient"/>
/// from <c>Motus.Runner.dll</c> with the test assembly's own <c>.deps.json</c> and
/// <c>.runtimeconfig.json</c>, the way <c>dotnet test</c> starts its test host. The test therefore
/// gets its own versions of everything it depends on, the framework assemblies it was built
/// against, and its own output folder as <see cref="AppContext.BaseDirectory"/>. It serves
/// discovery and test runs to the runner over a named pipe.
/// </summary>
internal static class TestHostServer
{
    /// <summary>The first argument that makes <c>Motus.Runner.dll</c> a test host rather than the visual runner.</summary>
    internal const string Command = "--motus-test-host";

    /// <summary>Asks the test host to stream the browser to the visual runner and record its timeline.</summary>
    internal const string VisualOption = "--visual";

    /// <summary>True when <paramref name="args"/> start a test host: the command, a pipe name and a test assembly.</summary>
    public static bool IsHostCommand(string[] args) => args.Length >= 3 && args[0] == Command;

    /// <summary>
    /// Makes the test assembly's folder the base directory and the current directory, as they are
    /// under <c>dotnet test</c>, and lets the runner's own assemblies load beside the test's. Called
    /// before anything uses a Motus type, since those resolve through it when the test was built
    /// against another Motus version or does not reference Motus at all.
    /// </summary>
    public static void Prepare(string[] args)
    {
        var assemblyPath = Path.GetFullPath(args[2]);
        var testDirectory = Path.GetDirectoryName(assemblyPath)!;
        var runnerDirectory = Path.GetDirectoryName(typeof(TestHostServer).Assembly.Location)!;

        // The host already does this when the test has a .deps.json, which sets the base directory
        // to its folder. A test without one still expects its own.
        AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", testDirectory + Path.DirectorySeparatorChar);

        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            // A version the test's dependencies don't have, such as the runner asking for a newer
            // Motus than the test references: the test's own copy is the one to use.
            if (name.Version is not null && name.Name is not null)
            {
                try { return context.LoadFromAssemblyName(new AssemblyName(name.Name)); }
                catch (Exception ex) when (ex is FileNotFoundException or FileLoadException) { }
            }

            // Not one of the test's dependencies: the runner's own, or a test without a .deps.json.
            foreach (var directory in (string[])[testDirectory, runnerDirectory])
            {
                var path = Path.Combine(directory, name.Name + ".dll");
                if (File.Exists(path))
                    return context.LoadFromAssemblyPath(path);
            }

            return null;
        };

        // The process starts in the folder motus run was started in. motus.config.json is read from
        // there, where the runner reads it, so that both see the same settings; it is read once per
        // process, so reading it now keeps it. Then the tests get their own folder, where
        // WebApplicationFactory looks for the app's manifest and the tests' relative paths resolve.
        try
        {
            MotusInterop.LoadConfig();
        }
        catch
        {
            // Motus reads its settings when it first needs them, from the folder below.
        }

        Environment.CurrentDirectory = testDirectory;
    }

    /// <summary>Serves the runner until it closes the pipe. Returns the process exit code.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        var pipeName = args[1];
        var assemblyPath = Path.GetFullPath(args[2]);
        var visual = args.Skip(3).Contains(VisualOption);

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"The Motus test host could not reach the runner: {ex.Message}");
            await pipe.DisposeAsync();
            return 1;
        }

        await using var connection = new TestHostConnection(pipe);
        await new Session(connection, assemblyPath, visual).ServeAsync();
        return 0;
    }

    private sealed class Session(TestHostConnection connection, string assemblyPath, bool visual)
    {
        private readonly ConcurrentDictionary<int, CancellationTokenSource> _runs = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource> _steps = new();
        private InProcessTestHost? _host;
        private int _nextStepId;

        public async Task ServeAsync()
        {
            MotusInterop.Warn = Log;

            if (visual)
                InstallVisualHooks();

            while (await connection.ReadAsync() is { } message)
            {
                switch (message.Type)
                {
                    case TestHostMessageTypes.Discover:
                        // Before anything else, and nothing else is sent until it has replied.
                        Reply(message, Discover(message.Name));
                        break;

                    case TestHostMessageTypes.Initialize:
                        _ = Task.Run(async () => Reply(message, new TestHostMessage
                        {
                            Type = TestHostMessageTypes.Initialized,
                            Failure = await Host.InitializeAsync(),
                        }));
                        break;

                    case TestHostMessageTypes.Run:
                        var cancellation = new CancellationTokenSource();
                        _runs[message.Id] = cancellation;
                        _ = Task.Run(async () =>
                        {
                            TestCaseReport report;
                            try
                            {
                                report = await Host.RunAsync(message.TestId ?? message.Name ?? "", message.Name ?? "", cancellation.Token);
                            }
                            catch (Exception ex)
                            {
                                report = TestCaseReport.NotRun(message.Name ?? "", ex.Message);
                            }
                            finally
                            {
                                _runs.TryRemove(message.Id, out _);
                            }

                            Reply(message, new TestHostMessage { Type = TestHostMessageTypes.Result, Report = report });
                        });
                        break;

                    case TestHostMessageTypes.Cancel:
                        if (_runs.TryGetValue(message.Id, out var running))
                            running.Cancel();
                        break;

                    case TestHostMessageTypes.Cleanup:
                        _ = Task.Run(async () =>
                        {
                            await Host.CleanupAsync();
                            Reply(message, new TestHostMessage { Type = TestHostMessageTypes.CleanedUp });
                        });
                        break;

                    case TestHostMessageTypes.StepContinue:
                        if (_steps.TryRemove(message.Id, out var step))
                            step.TrySetResult();
                        break;
                }
            }

            // The runner has closed the pipe, whether it finished or went away.
        }

        private InProcessTestHost Host => _host ?? throw new InvalidOperationException("No test assembly has been discovered.");

        private TestHostMessage Discover(string? filter)
        {
            try
            {
                var assembly = LoadTestAssembly();
                WarnAboutMotusVersion();
                var cases = TestCaseDiscovery.Discover(assembly, filter, Log);
                _host = new InProcessTestHost(assembly, cases, Log);

                return new TestHostMessage
                {
                    Type = TestHostMessageTypes.Discovered,
                    AssemblyName = assembly.GetName().Name,
                    Tests = cases.Select(c => c.ToInfo()).ToList(),
                };
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } e } ? e : ex;
                return new TestHostMessage
                {
                    Type = TestHostMessageTypes.Discovered,
                    Failure = new TestHostFailure(inner.Message, inner.StackTrace),
                };
            }
        }

        private Assembly LoadTestAssembly()
        {
            // With the test's .deps.json the assembly is one of the application's own, and loading
            // it by name gives the copy everything else binds to. Without one, it is loaded by path.
            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyName(AssemblyName.GetAssemblyName(assemblyPath));
            }
            catch (FileNotFoundException)
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
            }
        }

        /// <summary>
        /// The tests run on the Motus their project references, which the runner reaches into for
        /// its results. Says so when that is not the version this runner was built with.
        /// </summary>
        private void WarnAboutMotusVersion()
        {
            var expected = typeof(TestHostServer).Assembly.GetReferencedAssemblies().FirstOrDefault(a => a.Name == "Motus")?.Version;
            var actual = MotusInterop.LoadedMotusVersion();
            if (expected is null || actual is null || expected == actual)
                return;

            Log($"{Path.GetFileName(assemblyPath)} references Motus {actual.ToString(3)} and this motus is {expected.ToString(3)}. "
                + "The tests run on the Motus they reference. If results or reports are missing, use the same version of both.");
        }

        private void InstallVisualHooks()
        {
            try
            {
                MotusInterop.Guard("the visual runner's browser view and timeline", InstallVisualHooksCore);
            }
            catch (Exception ex)
            {
                Log($"The visual runner cannot show the browser for {Path.GetFileName(assemblyPath)}: {ex.Message}");
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void InstallVisualHooksCore()
        {
            // Compile what the hooks will run now, so that a Motus without something they use turns
            // the browser view off here rather than failing inside a test's browser action.
            PrepareMethods(typeof(ScreencastService), except: nameof(ScreencastService.HighlightElementAsync));
            PrepareMethods(typeof(TimelineRecorderHook), except: null);

            var screencast = new ScreencastService();
            screencast.FrameReceived += frame => connection.Post(new TestHostMessage { Type = TestHostMessageTypes.Frame, Text = frame });
            var timeline = new RemoteTimeline(entry => connection.Post(new TestHostMessage
            {
                Type = TestHostMessageTypes.Timeline,
                Entry = TimelineEntryMessage.From(entry),
            }));
            var stepDebug = new RemoteStepDebug(StepAsync);

            // The pages the tests open, the way the visual runner watches them in its own process.
            BrowserContext.GlobalPageCreated = page =>
            {
                _ = Task.Run(async () =>
                {
                    try { await screencast.AttachPageAsync(page); }
                    catch { /* best-effort */ }
                });

                page.Context.GetPluginContext().RegisterLifecycleHook(new TimelineRecorderHook(timeline, stepDebug));
            };
        }

        /// <summary>
        /// JIT-compiles the methods of <paramref name="type"/>, including the state machines and
        /// closures of its async methods and lambdas, apart from those of <paramref name="except"/>.
        /// </summary>
        private static void PrepareMethods(Type type, string? except)
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var types = new[] { type }.Concat(type.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                .Where(t => except is null || !t.Name.Contains($"<{except}>", StringComparison.Ordinal) && !t.IsGenericTypeDefinition);

            foreach (var candidate in types)
            {
                foreach (var method in candidate.GetMethods(all).Cast<MethodBase>().Concat(candidate.GetConstructors(all)))
                {
                    if (method.IsAbstract || method.ContainsGenericParameters || method.Name == except)
                        continue;
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                }
            }
        }

        private async Task StepAsync(string action, string? selector, CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextStepId);
            var step = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _steps[id] = step;
            connection.Post(new TestHostMessage { Type = TestHostMessageTypes.Step, Id = id, Text = action, Selector = selector });
            await step.Task.WaitAsync(cancellationToken);
        }

        private void Log(string message) => connection.Post(new TestHostMessage { Type = TestHostMessageTypes.Log, Text = message });

        private void Reply(TestHostMessage request, TestHostMessage reply)
        {
            reply.Id = request.Id;
            connection.Post(reply);
        }
    }

    /// <summary>
    /// The timeline the recorder hook writes to inside the test host. Entries go to the visual
    /// runner, which numbers them and names their test.
    /// </summary>
    private sealed class RemoteTimeline(Action<TimelineEntry> send) : ITimelineService
    {
        public IReadOnlyList<TimelineEntry> Entries => [];
        public int? SelectedIndex => null;
        public TimelineEntry? SelectedEntry => null;
        public string? CurrentTestName { get; set; }
        public string? SelectedTestName => null;
        public event Action? TimelineChanged { add { } remove { } }
        public event Action? TestSelected { add { } remove { } }
        public void Clear() { }
        public void AddEntry(TimelineEntry entry) => send(entry);
        public void SelectEntry(int index) { }
        public void ClearSelection() { }
        public void SelectTest(string fullName) { }
    }

    /// <summary>
    /// Step debugging inside the test host: each action asks the visual runner, which holds it
    /// while the user steps.
    /// </summary>
    private sealed class RemoteStepDebug(Func<string, string?, CancellationToken, Task> step) : IStepDebugService
    {
        public bool IsStepMode => false;
        public bool IsPaused => false;
        public string? PendingActionType => null;
        public string? PendingSelector => null;
        public event Action? StateChanged { add { } remove { } }
        public void EnableStepMode() { }
        public void DisableStepMode() { }
        public void Advance() { }
        public void Resume() { }
        public Task WaitIfPausedAsync(string actionType, string? selector, CancellationToken ct) => step(actionType, selector, ct);
    }
}
