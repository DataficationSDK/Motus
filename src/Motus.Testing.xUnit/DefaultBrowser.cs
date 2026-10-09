namespace Motus.Testing.xUnit;

/// <summary>
/// The browser shared by every <see cref="BrowserContextFixture"/> that was not given a
/// <see cref="SharedBrowserFixture"/>, one per test process.
/// </summary>
/// <remarks>
/// xUnit reads collection definitions only from the test assembly, so a fixture that ships in this
/// package cannot rely on one to launch its browser. This one launches when the first context is
/// asked of it, with the settings in motus.config.json and the environment, and is closed by
/// <see cref="BrowserFixture"/> when the process exits. xUnit has no assembly teardown to close it
/// sooner, and closing it whenever no class happened to be using it would relaunch it between
/// classes that run one after another.
/// </remarks>
internal static class DefaultBrowser
{
    internal static readonly BrowserFixture Fixture = new();
}
