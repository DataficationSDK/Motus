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
            + "--navigation-timeout 20000 --dialogs accept --config motus.config.json");

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
    public void Parse_TimeoutsAreNumbers()
    {
        Assert.AreEqual(0, Cmd.Parse("--timeout 5000 --navigation-timeout 20000").Errors.Count);
        Assert.IsTrue(Cmd.Parse("--timeout soon").Errors.Count > 0);
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
