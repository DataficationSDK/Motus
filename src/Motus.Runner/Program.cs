using Motus.Runner;
using Motus.Runner.Services.Execution;

// motus run starts this assembly as the test host for each test assembly (see TestHostServer).
if (TestHostServer.IsHostCommand(args))
{
    TestHostServer.Prepare(args);
    var exitCode = await TestHostServer.RunAsync(args);

    // A test can leave a foreground thread running, such as a server it never stopped, which
    // would otherwise keep the process alive after the runner has gone.
    Environment.Exit(exitCode);
}

await RunnerHost.StartAsync(args);
