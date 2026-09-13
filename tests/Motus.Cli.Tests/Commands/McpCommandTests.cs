using System.CommandLine;
using System.CommandLine.Parsing;
using System.Linq;
using Motus;
using Motus.Abstractions;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class McpCommandTests
{
    private static readonly Command Cmd = McpCommand.Build();

    [TestMethod]
    public void Parse_NoOptions_NoErrors()
    {
        var result = Cmd.Parse("");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_EveryLaunchOption_NoErrors()
    {
        var result = Cmd.Parse(
            "--executable-path /opt/browser --browser-arg=--no-sandbox --user-data-dir /tmp/profile "
            + "--storage-state state.json --proxy-server http://127.0.0.1:8080 --proxy-bypass localhost "
            + "--user-agent Agent/1.0 --locale en-GB --timezone Europe/Berlin --timeout 5000 "
            + "--navigation-timeout 20000 --settle 250 --dialogs accept --config motus.config.json "
            + "--caps recording");

        Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    [TestMethod]
    public void Parse_RepeatedBrowserArg_KeepsBoth()
    {
        var result = Cmd.Parse("--browser-arg=--no-sandbox --browser-arg=--disable-dev-shm-usage");

        Assert.AreEqual(0, result.Errors.Count);
        var option = (Option<string[]>)Cmd.Options.Single(o => o.Name == "--browser-arg");
        CollectionAssert.AreEqual(
            new[] { "--no-sandbox", "--disable-dev-shm-usage" }, result.GetValue(option));
    }

    [TestMethod]
    [DataRow("accept")]
    [DataRow("dismiss")]
    [DataRow("ask")]
    public void Parse_KnownDialogPolicy_NoErrors(string policy)
    {
        var result = Cmd.Parse($"--dialogs {policy}");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_UnknownDialogPolicy_HasError()
    {
        var result = Cmd.Parse("--dialogs shout");

        Assert.AreEqual(1, result.Errors.Count);
        StringAssert.Contains(result.Errors[0].Message, "accept, dismiss, ask");
    }

    [TestMethod]
    [DataRow("--caps recording")]
    [DataRow("--caps coordinates,recording,contexts,routing")]
    [DataRow("--caps contexts --caps routing")]
    public void Parse_KnownToolGroups_NoErrors(string commandLine)
    {
        var result = Cmd.Parse(commandLine);
        Assert.AreEqual(0, result.Errors.Count, string.Join("; ", result.Errors.Select(e => e.Message)));
    }

    /// <summary>
    /// A group name nobody recognizes is a typo in a client configuration, and the server would
    /// otherwise start with a quietly smaller catalog than was asked for.
    /// </summary>
    [TestMethod]
    public void Parse_UnknownToolGroup_HasError()
    {
        var result = Cmd.Parse("--caps recording,vision");

        Assert.AreEqual(1, result.Errors.Count);
        StringAssert.Contains(result.Errors[0].Message, "'vision'");
        StringAssert.Contains(result.Errors[0].Message, "coordinates, recording, contexts, routing");
    }

    [TestMethod]
    public void TryResolveCapabilities_ReadsCommasAndTrimsSpace()
    {
        Assert.IsTrue(McpCommand.TryResolveCapabilities(
            ["recording, contexts", "routing"], out var caps, out var error), error);

        CollectionAssert.AreEqual(new[] { "recording", "contexts", "routing" }, caps);
    }

    [TestMethod]
    public void TryResolveCapabilities_NothingAsked_IsTheDefaultCatalog()
    {
        Assert.IsTrue(McpCommand.TryResolveCapabilities(null, out var caps, out var error), error);
        Assert.AreEqual(0, caps.Length);
    }

    /// <summary>
    /// An MCP client's launch configuration is often easier to set a variable in than to edit an
    /// argument list, so the groups can be named that way when the flag is absent.
    /// </summary>
    [TestMethod]
    public void TryResolveCapabilities_FromTheEnvironment_ReadsCommaSeparatedNames()
    {
        Assert.IsTrue(
            McpCommand.TryResolveCapabilities(
                null, out var caps, out var error, envReader: Env("recording, contexts")),
            error);

        CollectionAssert.AreEqual(new[] { "recording", "contexts" }, caps);
    }

    /// <summary>
    /// An exported variable changes every server started from that shell, so what was typed for
    /// this one wins.
    /// </summary>
    [TestMethod]
    public void TryResolveCapabilities_FlagBeatsTheEnvironment()
    {
        Assert.IsTrue(
            McpCommand.TryResolveCapabilities(
                ["routing"], out var caps, out var error, envReader: Env("recording")),
            error);

        CollectionAssert.AreEqual(new[] { "routing" }, caps);
    }

    [TestMethod]
    public void TryResolveCapabilities_EnvironmentBeatsTheConfigFile()
    {
        Assert.IsTrue(
            McpCommand.TryResolveCapabilities(
                null, out var caps, out var error, fromConfig: ["routing"], envReader: Env("recording")),
            error);

        CollectionAssert.AreEqual(new[] { "recording" }, caps);
    }

    [TestMethod]
    public void TryResolveCapabilities_UnknownGroupFromTheEnvironment_NamesTheOnesThatExist()
    {
        Assert.IsFalse(McpCommand.TryResolveCapabilities(
            null, out _, out var error, envReader: Env("recording,telepathy")));

        StringAssert.Contains(error, "'telepathy'");
        StringAssert.Contains(error, "coordinates, recording, contexts, routing");
    }

    /// <summary>An environment reader that answers only for the variable this command reads.</summary>
    private static Func<string, string?> Env(string caps)
        => name => name == "MOTUS_MCP_CAPS" ? caps : null;

    [TestMethod]
    public void TryResolveCapabilities_UnknownGroup_NamesTheOnesThatExist()
    {
        Assert.IsFalse(McpCommand.TryResolveCapabilities(["pdf"], out _, out var error));
        StringAssert.Contains(error, "'pdf'");
        StringAssert.Contains(error, "coordinates, recording, contexts, routing");
    }

    /// <summary>
    /// A config file that names a group nobody recognizes is caught on the same terms as one typed
    /// on the command line, before a browser is resolved or a server started.
    /// </summary>
    [TestMethod]
    public async Task Invoke_ConfigNamesAnUnknownToolGroup_Fails()
    {
        var config = WriteTempFile("""{"mcp": {"caps": ["recording", "telepathy"]}}""", ".json");

        try
        {
            var (exit, stderr) = await RunAsync($"--config {config}");

            Assert.AreEqual(1, exit);
            StringAssert.Contains(stderr, "'telepathy'");
        }
        finally
        {
            File.Delete(config);
        }
    }

    [TestMethod]
    public void Parse_TimeoutsAreNumbers()
    {
        Assert.AreEqual(0, Cmd.Parse("--timeout 5000 --navigation-timeout 20000 --settle 0").Errors.Count);
        Assert.IsTrue(Cmd.Parse("--timeout soon").Errors.Count > 0);
    }

    [TestMethod]
    public void Parse_NegativeSettle_HasError()
    {
        var result = Cmd.Parse("--settle -1");

        Assert.AreEqual(1, result.Errors.Count);
        StringAssert.Contains(result.Errors[0].Message, "--settle");
    }

    [TestMethod]
    public async Task Invoke_UnknownChannel_ReportsTheKnownOnes()
    {
        var (exit, stderr) = await RunAsync("--channel netscape");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(stderr, "Unknown --channel value 'netscape'");
    }

    [TestMethod]
    public async Task Invoke_ExecutablePathThatIsNotThere_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "motus-no-such-browser-" + Guid.NewGuid().ToString("N"));
        var (exit, stderr) = await RunAsync($"--executable-path {missing}");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(stderr, "Browser executable not found");
    }

    /// <summary>
    /// A channel that was asked for by name and is not installed stops the server rather than
    /// falling through to whatever browser the machine happens to have.
    /// </summary>
    [TestMethod]
    public async Task Invoke_NamedChannelNotInstalled_FailsWithInstallAdvice()
    {
        var channel = FirstChannelNotInstalled();
        if (channel is null)
        {
            Assert.Inconclusive("Every browser channel is installed here, so there is nothing to fail on.");
            return;
        }

        var (exit, stderr) = await RunAsync($"--channel {channel}");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(stderr, $"No {channel} installation found.");
        StringAssert.Contains(stderr, $"motus install --channel {channel}");
        StringAssert.Contains(stderr, "--executable-path");
    }

    [TestMethod]
    public async Task Invoke_ConfigFileThatIsNotThere_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "motus-no-such-config-" + Guid.NewGuid().ToString("N") + ".json");
        var (exit, stderr) = await RunAsync($"--config {missing}");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(stderr, "Config file not found");
    }

    [TestMethod]
    public async Task Invoke_StorageStateFileThatIsNotThere_Fails()
    {
        var missing = Path.Combine(Path.GetTempPath(), "motus-no-such-state-" + Guid.NewGuid().ToString("N") + ".json");
        var (exit, stderr) = await RunAsync($"--storage-state {missing}");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(stderr, "Storage state file not found");
    }

    [TestMethod]
    public async Task Invoke_ProxyBypassWithoutProxyServer_Fails()
    {
        var (exit, stderr) = await RunAsync("--proxy-bypass localhost");

        Assert.AreEqual(1, exit);
        StringAssert.Contains(stderr, "--proxy-bypass needs --proxy-server");
    }

    [TestMethod]
    public void TryResolveBrowser_ChannelNobodyNamed_LeavesTheChoiceOpen()
    {
        var resolved = McpCommand.TryResolveBrowser(
            "chromium", channelNamed: false, executablePath: null,
            out var channel, out _, out var error);

        Assert.IsTrue(resolved, error);
        Assert.IsNull(channel, "An unnamed channel must not pin the launcher to one browser.");
    }

    [TestMethod]
    public void TryResolveBrowser_ExplicitExecutable_IsUsedAsIs()
    {
        var path = Path.Combine(Path.GetTempPath(), "motus-fake-browser-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(path, "not really a browser");
        try
        {
            var resolved = McpCommand.TryResolveBrowser(
                "firefox", channelNamed: true, executablePath: path,
                out var channel, out var resolvedPath, out var error);

            Assert.IsTrue(resolved, error);
            Assert.AreEqual(BrowserChannel.Firefox, channel);
            Assert.AreEqual(path, resolvedPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A config file is a source of defaults, not a second command line: it fills in what nobody
    /// typed and loses to everything that was.
    /// </summary>
    [TestMethod]
    public async Task Invoke_ConfigNamesAChannelThatIsNotInstalled_Fails()
    {
        // An executable path in the environment would stand in for the missing channel, and this
        // test is about the file alone.
        using var environment = WithoutEnvironmentVariable("MOTUS_EXECUTABLE_PATH");

        var channel = FirstChannelNotInstalled();
        if (channel is null)
        {
            Assert.Inconclusive("Every browser channel is installed here, so there is nothing to fail on.");
            return;
        }

        var config = WriteLaunchConfig("channel", channel);
        try
        {
            var (exit, stderr) = await RunAsync($"--config {config}");

            Assert.AreEqual(1, exit);
            StringAssert.Contains(stderr, $"No {channel} installation found.");
        }
        finally
        {
            File.Delete(config);
        }
    }

    [TestMethod]
    public async Task Invoke_ExecutablePathOnTheCommandLine_BeatsTheConfig()
    {
        var fromConfig = Path.Combine(Path.GetTempPath(), "motus-config-browser-" + Guid.NewGuid().ToString("N"));
        var fromCommandLine = Path.Combine(Path.GetTempPath(), "motus-typed-browser-" + Guid.NewGuid().ToString("N"));
        var config = WriteLaunchConfig("executablePath", fromConfig);

        try
        {
            // Neither path exists, so both stop the server. Which one the message names is the
            // answer being tested.
            var (exit, stderr) = await RunAsync($"--config {config} --executable-path {fromCommandLine}");

            Assert.AreEqual(1, exit);
            StringAssert.Contains(stderr, fromCommandLine);
        }
        finally
        {
            File.Delete(config);
        }
    }

    [TestMethod]
    public async Task Invoke_ConfigExecutablePath_IsUsedWhenNobodyTypedOne()
    {
        // The environment sits between the command line and the config file, and a build that pins
        // its browser exports the executable path to every process it starts. This test is about
        // the file alone, so the variable is taken out of the picture rather than assumed absent.
        using var environment = WithoutEnvironmentVariable("MOTUS_EXECUTABLE_PATH");

        var fromConfig = Path.Combine(Path.GetTempPath(), "motus-config-browser-" + Guid.NewGuid().ToString("N"));
        var config = WriteLaunchConfig("executablePath", fromConfig);

        try
        {
            var (exit, stderr) = await RunAsync($"--config {config}");

            Assert.AreEqual(1, exit);
            StringAssert.Contains(stderr, fromConfig);
        }
        finally
        {
            File.Delete(config);
        }
    }

    [TestMethod]
    public void LoadStorageState_ReadsWhatTheContextWrote()
    {
        var path = WriteTempFile(
            """{"Cookies":[{"Name":"session","Value":"abc","Domain":"example.com","Path":"/"}],"Origins":[]}""",
            ".json");

        try
        {
            var state = McpCommand.LoadStorageState(path, out var error);

            Assert.IsNotNull(state, error);
            Assert.AreEqual(1, state.Cookies.Count);
            Assert.AreEqual("session", state.Cookies[0].Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void LoadStorageState_FileThatIsNotJson_ExplainsItself()
    {
        var path = WriteTempFile("not json at all", ".json");

        try
        {
            Assert.IsNull(McpCommand.LoadStorageState(path, out var error));
            StringAssert.Contains(error, "Could not read storage state from");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Writes a config file that names one launch setting, and returns its path.</summary>
    private static string WriteLaunchConfig(string key, string value)
    {
        var escaped = value.Replace("\\", "\\\\");
        return WriteTempFile("{\"launch\": {\"" + key + "\": \"" + escaped + "\"}}", ".json");
    }

    private static string WriteTempFile(string content, string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), "motus-mcp-test-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Runs the command and returns its exit code along with what it wrote to standard error.
    /// </summary>
    /// <summary>
    /// Clears an environment variable for the duration of a test and puts it back afterwards.
    /// </summary>
    private static IDisposable WithoutEnvironmentVariable(string name)
    {
        var previous = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, null);
        return new RestoreEnvironmentVariable(name, previous);
    }

    private sealed class RestoreEnvironmentVariable(string name, string? value) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, value);
    }

    private static async Task<(int Exit, string Stderr)> RunAsync(string commandLine)
    {
        var captured = new StringWriter();
        var original = Console.Error;
        try
        {
            Console.SetError(captured);
            var exit = await Cmd.Parse(commandLine).InvokeAsync();
            return (exit, captured.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    /// <summary>
    /// The first channel this machine has no browser for, or null when it has them all. Which
    /// browsers are present is a property of the machine, so the test that needs a missing one asks
    /// rather than assuming.
    /// </summary>
    private static string? FirstChannelNotInstalled()
    {
        foreach (var channel in Enum.GetValues<BrowserChannel>())
        {
            var name = channel.ToString().ToLowerInvariant();
            if (BrowserPathHelper.ResolveChannel(name) is null
                && !BrowserFinder.CandidatesForChannel(channel).Any(File.Exists))
            {
                return name;
            }
        }

        return null;
    }
}
