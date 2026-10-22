using System.Collections.Concurrent;
using System.Text;

namespace Motus.Tests.Testing;

[TestClass]
public class FailureArtifactNameTests
{
    private static readonly DateTime s_stamp = new(2026, 10, 4, 22, 15, 1, 123, DateTimeKind.Utc);

    private static readonly char[] s_rejected = ['/', '\\', ':', '"', '<', '>', '|', '*', '?', ' '];

    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), "motus-artifact-name-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }

    [TestMethod]
    public void Sanitize_ReplacesCharactersWindowsAndCiRejects()
    {
        var a = FailureArtifactName.Sanitize("Tests.Rows(\"a/b:c\",1)")!;
        var b = FailureArtifactName.Sanitize("Tests.x \"y\" <z>|*?\\w")!;

        foreach (var label in new[] { a, b })
        {
            Assert.AreEqual(-1, label.IndexOfAny(s_rejected), $"'{label}' kept a character some systems reject");
            StringAssert.DoesNotMatch(label, new System.Text.RegularExpressions.Regex("__"), $"'{label}' kept a run of underscores");
        }

        Assert.AreEqual("Tests.Rows_a_b_c_1", a);
        Assert.AreEqual("Tests.x_y_z_w", b);
    }

    [TestMethod]
    public void Sanitize_TrimsLeadingAndTrailingDotsAndSeparators()
    {
        Assert.AreEqual("Name", FailureArtifactName.Sanitize(" ..Name. "));
        Assert.AreEqual("Name", FailureArtifactName.Sanitize("__Name__"));
    }

    [TestMethod]
    public void Sanitize_KeepsUnicodeLetters()
    {
        Assert.AreEqual("Tests.Größe_日本語", FailureArtifactName.Sanitize("Tests.Größe 日本語"));
    }

    [TestMethod]
    public void Sanitize_GenericTypeNames()
    {
        Assert.AreEqual("Foo_1.Bar", FailureArtifactName.Sanitize("Foo`1.Bar"));
    }

    [TestMethod]
    public void Sanitize_NothingUsable_ReturnsNull()
    {
        Assert.IsNull(FailureArtifactName.Sanitize(null));
        Assert.IsNull(FailureArtifactName.Sanitize("   "));
        Assert.IsNull(FailureArtifactName.Sanitize("<>|*?"));
    }

    [TestMethod]
    public void Build_WithLabel_NamesTheTest()
    {
        var name = FailureArtifactName.Build("trace", "SaveReplaceDialogTests.EnterOnCancel", "zip", s_stamp);

        Assert.AreEqual("trace-SaveReplaceDialogTests.EnterOnCancel-20261004-221501-123.zip", name);
    }

    [TestMethod]
    public void Build_WithoutLabel_KeepsTheTimestampOnlyName()
    {
        Assert.AreEqual("trace-20261004-221501-123.zip", FailureArtifactName.Build("trace", null, "zip", s_stamp));
        Assert.AreEqual("failure-20261004-221501-123.png", FailureArtifactName.Build("failure", null, "png", s_stamp));
    }

    [TestMethod]
    public void LongLabel_IsCappedAndKeptDistinct()
    {
        var common = new string('A', 120);
        var first = FailureArtifactName.Sanitize(common + ".First")!;
        var second = FailureArtifactName.Sanitize(common + ".Second")!;

        Assert.IsTrue(Encoding.UTF8.GetByteCount(first) <= FailureArtifactName.MaxLabelBytes, first);
        Assert.AreNotEqual(first, second, "labels sharing a long prefix should not collapse to one name");

        var fileName = FailureArtifactName.Build("trace", FailureArtifactName.Sanitize(new string('x', 400)), "zip", s_stamp);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(fileName) <= 255, fileName);
    }

    [TestMethod]
    public void LongLabel_IsCutOnACharacterBoundary()
    {
        // Three-byte characters straddle the cut point unless it respects them.
        var label = FailureArtifactName.Sanitize(new string('日', 60))!;

        Assert.IsFalse(label.Contains('�'), label);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(label) <= FailureArtifactName.MaxLabelBytes, label);
    }

    [TestMethod]
    public void ReservePath_CreatesTheFileAndReturnsAFullPath()
    {
        var path = FailureArtifactName.ReservePath(_dir, "trace-T.M-20261004-221501-123.zip");

        Assert.IsTrue(Path.IsPathFullyQualified(path), path);
        Assert.IsTrue(File.Exists(path), "the name should be claimed on disk");
        Assert.AreEqual("trace-T.M-20261004-221501-123.zip", Path.GetFileName(path));
    }

    [TestMethod]
    public void ReservePath_AppendsASuffixOnCollision()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "trace-T.M-1.zip"), "earlier failure");

        var path = FailureArtifactName.ReservePath(_dir, "trace-T.M-1.zip");

        Assert.AreEqual("trace-T.M-1-2.zip", Path.GetFileName(path));
        Assert.AreEqual("earlier failure", File.ReadAllText(Path.Combine(_dir, "trace-T.M-1.zip")),
            "the earlier artifact must not be overwritten");
    }

    [TestMethod]
    public void ReservePath_ParallelCallersGetDistinctPaths()
    {
        var paths = new ConcurrentBag<string>();

        Parallel.For(0, 20, _ => paths.Add(FailureArtifactName.ReservePath(_dir, "failure-T.Rows-1.png")));

        Assert.AreEqual(20, paths.Distinct(StringComparer.Ordinal).Count());
    }
}
