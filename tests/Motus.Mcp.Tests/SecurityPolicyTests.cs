using Motus.Mcp;

namespace Motus.Mcp.Tests;

/// <summary>
/// Pins the boundaries the server keeps around the machine it runs on: where a tool that writes a
/// file may put it, where a tool that reads one may take it from, which URLs may be opened, and
/// whether the session may be pointed at a browser that is already running.
/// </summary>
[TestClass]
public class SecurityPolicyTests
{
    private string _root = string.Empty;
    private string _outputDir = string.Empty;
    private string _outside = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        // One tree per test: the net8.0 and net10.0 test assemblies run as separate processes and
        // would otherwise contend for one fixed path under the temp dir.
        _root = Path.Combine(Path.GetTempPath(), $"motus_policy_{Guid.NewGuid():N}");
        _outputDir = Path.Combine(_root, "output");
        _outside = Path.Combine(_root, "outside");
        Directory.CreateDirectory(_outputDir);
        Directory.CreateDirectory(_outside);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    private SecurityPolicy Policy(bool unrestricted = false, bool allowAttach = false, string? endpoint = null)
        => new(new McpServerLaunchOptions
        {
            OutputDirectory = _outputDir,
            AllowUnrestrictedFileAccess = unrestricted,
            AllowAttach = allowAttach,
            Endpoint = endpoint,
        });

    // --- where a tool may write ---

    [TestMethod]
    public void OutputPath_APlainName_LandsInTheOutputDirectory()
    {
        Assert.IsTrue(Policy().TryResolveOutputPath("report.har", "motus", ".har", out var resolved, out var refusal));

        Assert.IsNull(refusal);
        Assert.AreEqual(Path.Combine(_outputDir, "report.har"), resolved);
        Assert.IsTrue(Directory.Exists(_outputDir), "the directory should exist by the time a tool writes into it");
    }

    [TestMethod]
    public void OutputPath_ASubdirectory_IsAllowed()
    {
        Assert.IsTrue(Policy().TryResolveOutputPath(
            Path.Combine("runs", "first.har"), "motus", ".har", out var resolved, out _));

        Assert.AreEqual(Path.Combine(_outputDir, "runs", "first.har"), resolved);
    }

    [TestMethod]
    public void OutputPath_OmittedEntirely_IsGeneratedInTheOutputDirectory()
    {
        Assert.IsTrue(Policy().TryResolveOutputPath(null, "motus-trace", ".zip", out var resolved, out _));

        StringAssert.StartsWith(resolved, _outputDir);
        StringAssert.Contains(Path.GetFileName(resolved), "motus-trace");
        StringAssert.EndsWith(resolved, ".zip");
    }

    [TestMethod]
    public void OutputPath_AnAbsolutePath_IsRefused()
    {
        var elsewhere = Path.Combine(_outside, "taken.har");

        Assert.IsFalse(Policy().TryResolveOutputPath(elsewhere, "motus", ".har", out _, out var refusal));

        StringAssert.Contains(refusal, "must stay inside the output directory");
        StringAssert.Contains(refusal, _outputDir);
        StringAssert.Contains(refusal, "--allow-unrestricted-file-access");
    }

    [TestMethod]
    public void OutputPath_APathThatClimbsOut_IsRefused()
    {
        Assert.IsFalse(Policy().TryResolveOutputPath(
            Path.Combine("..", "outside", "taken.har"), "motus", ".har", out _, out var refusal));

        StringAssert.Contains(refusal, "must stay inside the output directory");
    }

    [TestMethod]
    public void OutputPath_ThroughASymbolicLinkInTheOutputDirectory_IsRefused()
    {
        // The link sits inside the output directory, so the path reads as an innocent relative one
        // right up until the filesystem follows it out of the directory.
        Directory.CreateSymbolicLink(Path.Combine(_outputDir, "escape"), _outside);

        Assert.IsFalse(Policy().TryResolveOutputPath(
            Path.Combine("escape", "taken.har"), "motus", ".har", out _, out var refusal));

        StringAssert.Contains(refusal, "must stay inside the output directory");
    }

    [TestMethod]
    public void OutputPath_ThroughASymbolicLinkThatStaysInside_IsAllowed()
    {
        var inner = Path.Combine(_outputDir, "runs");
        Directory.CreateDirectory(inner);
        Directory.CreateSymbolicLink(Path.Combine(_outputDir, "latest"), inner);

        Assert.IsTrue(Policy().TryResolveOutputPath(
            Path.Combine("latest", "first.har"), "motus", ".har", out _, out var refusal), refusal);
    }

    [TestMethod]
    public void OutputPath_WithUnrestrictedFileAccess_TakesAnyPath()
    {
        var elsewhere = Path.Combine(_outside, "taken.har");

        Assert.IsTrue(Policy(unrestricted: true)
            .TryResolveOutputPath(elsewhere, "motus", ".har", out var resolved, out _));

        Assert.AreEqual(elsewhere, resolved);
    }

    // --- where a tool may read ---

    [TestMethod]
    public async Task Read_InsideAReportedRoot_IsAllowed()
    {
        var policy = new SecurityPolicy(new McpServerLaunchOptions { OutputDirectory = _outputDir })
        {
            ReadRootsOverride = _ => new ValueTask<IReadOnlyList<string>>(new[] { _outside }),
        };

        Assert.IsNull(await policy.RefuseReadAsync(
            Path.Combine(_outside, "upload.txt"), server: null, CancellationToken.None));
    }

    [TestMethod]
    public async Task Read_OutsideEveryReportedRoot_IsRefused()
    {
        var policy = new SecurityPolicy(new McpServerLaunchOptions { OutputDirectory = _outputDir })
        {
            ReadRootsOverride = _ => new ValueTask<IReadOnlyList<string>>(new[] { _outside }),
        };

        var refusal = await policy.RefuseReadAsync(
            Path.Combine(_root, "elsewhere.txt"), server: null, CancellationToken.None);

        StringAssert.Contains(refusal, "Reads are confined to");
        StringAssert.Contains(refusal, "--allow-unrestricted-file-access");
    }

    [TestMethod]
    public async Task Read_ThroughASymbolicLinkOutOfAReportedRoot_IsRefused()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "secret.txt"), "secret");
        File.CreateSymbolicLink(Path.Combine(_outside, "innocent.txt"), Path.Combine(_root, "secret.txt"));

        var policy = new SecurityPolicy(new McpServerLaunchOptions { OutputDirectory = _outputDir })
        {
            ReadRootsOverride = _ => new ValueTask<IReadOnlyList<string>>(new[] { _outside }),
        };

        var refusal = await policy.RefuseReadAsync(
            Path.Combine(_outside, "innocent.txt"), server: null, CancellationToken.None);

        StringAssert.Contains(refusal, "Reads are confined to");
    }

    /// <summary>
    /// A client may move its roots while the session is running. An answer kept from an earlier
    /// call would go on allowing a directory the client has since let go of, so every check asks
    /// again.
    /// </summary>
    [TestMethod]
    public async Task Read_WhenTheClientMovesItsRoots_FollowsTheChange()
    {
        var asked = 0;
        var policy = new SecurityPolicy(new McpServerLaunchOptions { OutputDirectory = _outputDir })
        {
            ReadRootsOverride = _ => new ValueTask<IReadOnlyList<string>>(
                ++asked == 1 ? new[] { _outside } : new[] { _outputDir }),
        };

        Assert.IsNull(await policy.RefuseReadAsync(
            Path.Combine(_outside, "upload.txt"), server: null, CancellationToken.None));

        var refusal = await policy.RefuseReadAsync(
            Path.Combine(_outside, "upload.txt"), server: null, CancellationToken.None);

        Assert.IsNotNull(refusal, "the client no longer reports that directory, so the read is refused.");
        Assert.IsNull(
            await policy.RefuseReadAsync(
                Path.Combine(_outputDir, "upload.txt"), server: null, CancellationToken.None),
            "and the directory it moved to is readable without restarting anything.");
    }

    [TestMethod]
    public async Task Read_WithNoRootsReported_FallsBackToTheWorkingAndOutputDirectories()
    {
        var policy = Policy();

        var roots = await policy.ReadRootsAsync(server: null, CancellationToken.None);

        CollectionAssert.AreEquivalent(
            new[] { Path.GetFullPath(Directory.GetCurrentDirectory()), Path.GetFullPath(_outputDir) },
            roots.ToArray());
        Assert.IsNull(await policy.RefuseReadAsync(
            Path.Combine(_outputDir, "produced.txt"), server: null, CancellationToken.None));
    }

    [TestMethod]
    public async Task Read_WithUnrestrictedFileAccess_TakesAnyPath()
    {
        Assert.IsNull(await Policy(unrestricted: true).RefuseReadAsync(
            Path.Combine(_root, "anything.txt"), server: null, CancellationToken.None));
    }

    // --- which URLs may be opened ---

    [TestMethod]
    [DataRow("file:///etc/hosts")]
    [DataRow("FILE:///etc/hosts")]
    [DataRow("  file://localhost/etc/hosts")]
    public void Url_PointingAtTheLocalFilesystem_IsRefused(string url)
    {
        var refusal = Policy().RefuseUrl(url);

        StringAssert.Contains(refusal, "file:// navigation is disabled");
        StringAssert.Contains(refusal, "--allow-unrestricted-file-access");
    }

    [TestMethod]
    [DataRow("https://example.com")]
    [DataRow("example.com")]
    [DataRow("data:text/html,<h1>hi</h1>")]
    [DataRow(null)]
    public void Url_AnythingElse_IsLeftAlone(string? url) => Assert.IsNull(Policy().RefuseUrl(url));

    [TestMethod]
    public void Url_WithUnrestrictedFileAccess_AllowsTheLocalFilesystem()
        => Assert.IsNull(Policy(unrestricted: true).RefuseUrl("file:///etc/hosts"));

    [TestMethod]
    public void Headers_RedirectingToTheLocalFilesystem_AreRefused()
    {
        var refusal = Policy().RefuseHeaders(new Dictionary<string, string> { ["location"] = "file:///etc/hosts" });

        StringAssert.Contains(refusal, "file:// navigation is disabled");
    }

    [TestMethod]
    public void Headers_RedirectingElsewhere_AreLeftAlone()
        => Assert.IsNull(Policy().RefuseHeaders(new Dictionary<string, string>
        {
            ["Location"] = "https://example.com",
            ["Content-Type"] = "text/html",
        }));

    // --- attaching to a browser that is already running ---

    [TestMethod]
    public void Attach_ByDefault_IsRefusedAndNamesTheOption()
    {
        var refusal = Policy().RefuseAttach();

        StringAssert.Contains(refusal, "--allow-attach");
        StringAssert.Contains(refusal, "--connect");
    }

    [TestMethod]
    public void Attach_WithTheOption_IsAllowed() => Assert.IsNull(Policy(allowAttach: true).RefuseAttach());

    [TestMethod]
    public void Attach_WhenTheServerAlreadyStartedAttached_IsAllowed()
        => Assert.IsNull(Policy(endpoint: "http://127.0.0.1:9222").RefuseAttach());

    // --- the default directory ---

    [TestMethod]
    public void DefaultOutputDirectory_IsNamedUnderTheTemporaryDirectoryAndNotCreatedYet()
    {
        var directory = SecurityPolicy.CreateDefaultOutputDirectory();

        StringAssert.StartsWith(directory, Path.GetTempPath());
        StringAssert.Contains(Path.GetFileName(directory), "motus-mcp-");
        Assert.IsFalse(Directory.Exists(directory), "naming a directory should not create it");
        Assert.AreNotEqual(directory, SecurityPolicy.CreateDefaultOutputDirectory());
    }
}
