using Motus.Runner.Services.Execution;

namespace Motus.Tests.Runner;

[TestClass]
public class TestHostLauncherTests
{
    [TestMethod]
    [DataRow("net8.0", true)]
    [DataRow("net8.0-windows", true)]
    [DataRow("net10.0", false)]
    [DataRow("net11.0", false)]
    [DataRow("netcoreapp3.1", true)]
    public void NeedsNewerRuntime_WhenTheSuiteTargetsAnOlderDotNetThanTheRunner(string tfm, bool expectedOnNet10)
    {
        var path = Path.Combine(Path.GetTempPath(), $"motus-{Guid.NewGuid():N}.runtimeconfig.json");
        File.WriteAllText(path, $$"""{ "runtimeOptions": { "tfm": "{{tfm}}" } }""");
        try
        {
            // The runner here is the build for the framework these tests run on.
            var expected = Environment.Version.Major >= 10 ? expectedOnNet10 : tfm == "netcoreapp3.1";
            Assert.AreEqual(expected, TestHostLauncher.NeedsNewerRuntime(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void NeedsNewerRuntime_WhenTheRuntimeConfigCannotBeRead()
        => Assert.IsTrue(TestHostLauncher.NeedsNewerRuntime(Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.json")));

    [TestMethod]
    public void StartInfo_UsesTheSuitesOwnDepsAndRuntimeConfig()
    {
        var assembly = typeof(TestHostLauncherTests).Assembly.Location;
        var stem = Path.Combine(Path.GetDirectoryName(assembly)!, Path.GetFileNameWithoutExtension(assembly));

        var startInfo = TestHostLauncher.CreateStartInfo("pipe", assembly, visual: true);
        var args = startInfo.ArgumentList.ToList();

        Assert.AreEqual("exec", args[0]);
        Assert.AreEqual(stem + ".runtimeconfig.json", args[args.IndexOf("--runtimeconfig") + 1]);
        Assert.AreEqual(stem + ".deps.json", args[args.IndexOf("--depsfile") + 1]);
        CollectionAssert.IsSubsetOf(new[] { TestHostServer.Command, "pipe", assembly, TestHostServer.VisualOption }, args);
        Assert.IsTrue(File.Exists(startInfo.FileName), startInfo.FileName);
    }
}
