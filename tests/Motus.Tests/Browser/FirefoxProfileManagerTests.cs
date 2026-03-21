namespace Motus.Tests.Browser;

[TestClass]
public class FirefoxProfileManagerTests
{
    [TestMethod]
    public void CreateTempProfile_CreatesDirectory()
    {
        var (profileDir, ownsTempDir) = FirefoxProfileManager.CreateTempProfile(null);

        try
        {
            Assert.IsTrue(Directory.Exists(profileDir));
            Assert.IsTrue(ownsTempDir);
            StringAssert.Contains(profileDir, "motus-firefox-");
        }
        finally
        {
            if (Directory.Exists(profileDir))
                Directory.Delete(profileDir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateTempProfile_WritesUserJs_WithRequiredPrefs()
    {
        var (profileDir, _) = FirefoxProfileManager.CreateTempProfile(null);

        try
        {
            var userJsPath = Path.Combine(profileDir, "user.js");
            Assert.IsTrue(File.Exists(userJsPath));

            var content = File.ReadAllText(userJsPath);
            StringAssert.Contains(content, "remote.active-protocols");
            StringAssert.Contains(content, "remote.enabled");
            StringAssert.Contains(content, "remote.allow-hosts");
        }
        finally
        {
            if (Directory.Exists(profileDir))
                Directory.Delete(profileDir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateTempProfile_WithUserDataDir_DoesNotOverwriteExistingUserJs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "motus-test-profile-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(tempDir);
            var userJsPath = Path.Combine(tempDir, "user.js");
            File.WriteAllText(userJsPath, "// custom user prefs");

            var (profileDir, ownsTempDir) = FirefoxProfileManager.CreateTempProfile(tempDir);

            Assert.AreEqual(tempDir, profileDir);
            Assert.IsFalse(ownsTempDir);

            // Should not overwrite existing user.js
            var content = File.ReadAllText(userJsPath);
            Assert.AreEqual("// custom user prefs", content);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public void CreateTempProfile_WithUserDataDir_CreatesDirectoryIfNotExists()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "motus-test-profile-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Assert.IsFalse(Directory.Exists(tempDir));

            var (profileDir, ownsTempDir) = FirefoxProfileManager.CreateTempProfile(tempDir);

            Assert.AreEqual(tempDir, profileDir);
            Assert.IsFalse(ownsTempDir);
            Assert.IsTrue(Directory.Exists(tempDir));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
