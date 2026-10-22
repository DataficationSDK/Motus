using Motus.Abstractions;

namespace Motus.Tests.Transport;

[TestClass]
public class ProtocolLogSettingsTests
{
    private static Func<string, string?> Env(string? value)
        => name => name == ProtocolLogSettings.EnvironmentVariable ? value : null;

    [TestMethod]
    public void Unset_IsOff()
    {
        Assert.IsNull(ProtocolLogSettings.Resolve(null, Env(null)));
        Assert.IsNull(ProtocolLogSettings.Resolve(null, Env("")));
        Assert.IsNull(ProtocolLogSettings.Resolve(null, Env("   ")));
    }

    [TestMethod]
    public void Stderr_CaseInsensitive()
    {
        var resolved = ProtocolLogSettings.Resolve(null, Env("STDERR"));

        Assert.IsNotNull(resolved);
        Assert.IsTrue(ProtocolLogSettings.IsStandardError(resolved.Path!));
    }

    [TestMethod]
    public void Stdout_IsRefused_WithWarning()
    {
        var warnings = new StringWriter();

        var resolved = ProtocolLogSettings.Resolve(null, Env("stdout"), warnings);

        Assert.IsNull(resolved);
        StringAssert.Contains(warnings.ToString(), "Motus: the protocol log cannot be written to stdout");
    }

    [TestMethod]
    public void Stdout_InCode_IsRefused_ButSinkKept()
    {
        var warnings = new StringWriter();
        Action<string> sink = _ => { };

        var resolved = ProtocolLogSettings.Resolve(
            new ProtocolLogOptions { Path = "Stdout", Sink = sink }, Env(null), warnings);

        Assert.IsNotNull(resolved);
        Assert.IsNull(resolved.Path);
        Assert.AreSame(sink, resolved.Sink);
        StringAssert.Contains(warnings.ToString(), "stdout");
    }

    [TestMethod]
    public void Path_IsFile()
    {
        var resolved = ProtocolLogSettings.Resolve(null, Env(" /tmp/motus.log "));

        Assert.AreEqual("/tmp/motus.log", resolved?.Path);
        Assert.AreEqual(1024, resolved!.MaxValueLength);
        Assert.IsTrue(resolved.RedactSecrets);
    }

    [TestMethod]
    public void ExplicitOptions_WinOverEnv()
    {
        var resolved = ProtocolLogSettings.Resolve(
            new ProtocolLogOptions { Path = "from-code.log" }, Env("from-env.log"));

        Assert.AreEqual("from-code.log", resolved?.Path);
    }

    [TestMethod]
    public void ExplicitOptions_WithoutDestination_TakeDestinationFromEnv()
    {
        var resolved = ProtocolLogSettings.Resolve(
            new ProtocolLogOptions { MaxValueLength = 50, RedactSecrets = false }, Env("from-env.log"));

        Assert.AreEqual("from-env.log", resolved?.Path);
        Assert.AreEqual(50, resolved!.MaxValueLength);
        Assert.IsFalse(resolved.RedactSecrets);
    }

    [TestMethod]
    public void ExplicitOptions_WithoutDestination_AndNoEnv_IsOff()
    {
        Assert.IsNull(ProtocolLogSettings.Resolve(new ProtocolLogOptions(), Env(null)));
    }

    [TestMethod]
    public void EnvironmentVariable_CannotTurnRedactionOff()
    {
        var resolved = ProtocolLogSettings.Resolve(null, name => name switch
        {
            ProtocolLogSettings.EnvironmentVariable => "p.log",
            _ => "false",
        });

        Assert.IsTrue(resolved!.RedactSecrets);
    }
}
