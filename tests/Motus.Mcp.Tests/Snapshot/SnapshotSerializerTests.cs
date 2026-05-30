using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Snapshot;

[TestClass]
public class SnapshotSerializerTests
{
    private static AccessibilityNode Node(
        string? role, string? name, long? backendId,
        IReadOnlyList<AccessibilityNode>? children = null,
        string? value = null,
        IReadOnlyDictionary<string, string?>? properties = null)
        => new(
            NodeId: backendId?.ToString() ?? "x",
            Role: role,
            Name: name,
            Value: value,
            Description: null,
            Properties: properties ?? new Dictionary<string, string?>(),
            Children: children ?? [],
            BackendDOMNodeId: backendId);

    private static AccessibilitySnapshot Snapshot(params AccessibilityNode[] roots)
        => new(roots, IgnoredCount: 0, DiagnosticMessage: null);

    [TestMethod]
    public void AssignsRefs_InDocumentOrder()
    {
        var snapshot = Snapshot(Node("form", "Login", backendId: null, children:
        [
            Node("textbox", "Username", backendId: 10),
            Node("textbox", "Password", backendId: 11),
            Node("button", "Sign In", backendId: 12),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual(10, result.RefToBackendNodeId["e1"]);
        Assert.AreEqual(11, result.RefToBackendNodeId["e2"]);
        Assert.AreEqual(12, result.RefToBackendNodeId["e3"]);
        Assert.AreEqual(3, result.RefToBackendNodeId.Count);
    }

    [TestMethod]
    public void NodeWithoutBackendId_GetsNoRef_ButStillRenders()
    {
        var snapshot = Snapshot(Node("form", "Login", backendId: null, children:
        [
            Node("textbox", "Username", backendId: 10),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        StringAssert.Contains(result.Text, "- form \"Login\"");
        Assert.IsFalse(result.Text.Split('\n')[0].Contains("[ref="), "Root form must not carry a ref.");
        Assert.AreEqual(1, result.RefToBackendNodeId.Count, "Only the node with a backend id is refed.");
        Assert.AreEqual(10, result.RefToBackendNodeId["e1"]);
    }

    [TestMethod]
    public void RendersExpected_IndentedText()
    {
        var snapshot = Snapshot(Node("form", "Login", backendId: null, children:
        [
            Node("textbox", "Username", backendId: 10),
            Node("textbox", "Password", backendId: 11),
            Node("button", "Sign In", backendId: 12),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        var expected =
            "- form \"Login\"\n" +
            "  - textbox \"Username\" [ref=e1]\n" +
            "  - textbox \"Password\" [ref=e2]\n" +
            "  - button \"Sign In\" [ref=e3]\n";

        Assert.AreEqual(expected, result.Text);
    }

    [TestMethod]
    public void RendersStateFlags_WhenPropertyIsTrue()
    {
        var snapshot = Snapshot(Node("button", "Submit", backendId: 7,
            properties: new Dictionary<string, string?> { ["disabled"] = "true" }));

        var result = SnapshotSerializer.Serialize(snapshot);

        StringAssert.Contains(result.Text, "- button \"Submit\" [ref=e1] [disabled]");
    }

    [TestMethod]
    public void OmitsStateFlags_WhenPropertyIsFalse()
    {
        var snapshot = Snapshot(Node("checkbox", "Agree", backendId: 7,
            properties: new Dictionary<string, string?> { ["checked"] = "false" }));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.IsFalse(result.Text.Contains("[checked]"));
    }

    [TestMethod]
    public void NullRole_RendersAsGeneric()
    {
        var result = SnapshotSerializer.Serialize(Snapshot(Node(role: null, name: null, backendId: null)));
        StringAssert.StartsWith(result.Text, "- generic");
    }

    [TestMethod]
    public void Deterministic_AcrossRepeatedRuns()
    {
        var snapshot = Snapshot(Node("form", null, backendId: null, children:
        [
            Node("textbox", "A", backendId: 1),
            Node("textbox", "B", backendId: 2),
        ]));

        var first = SnapshotSerializer.Serialize(snapshot);
        var second = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual(first.Text, second.Text);
        CollectionAssert.AreEquivalent(
            first.RefToBackendNodeId.ToList(),
            second.RefToBackendNodeId.ToList());
    }
}
