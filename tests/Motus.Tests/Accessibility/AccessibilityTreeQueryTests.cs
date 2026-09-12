using System.Text.Json;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Accessibility;

[TestClass]
public class AccessibilityTreeQueryTests
{
    [TestMethod]
    public async Task GetTreeAsync_BiDiTransport_ReturnsEmptyTreeWithDiagnostic()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, "ctx-1");

        var query = new AccessibilityTreeQuery(session);
        var result = await query.GetTreeAsync(CancellationToken.None);

        Assert.AreEqual(0, result.AllWalkableNodes.Count);
        Assert.AreEqual(0, result.Roots.Count);
        Assert.AreEqual(0, result.IgnoredCount);
        Assert.IsNotNull(result.DiagnosticMessage);
        Assert.IsTrue(result.DiagnosticMessage.Contains("not supported"));
    }
}

/// <summary>
/// Covers how the flat node list the browser sends becomes a tree. The cases here are the shapes
/// a real page produces: markup nests controls inside layout elements that carry no meaning, and
/// the browser marks those as ignored rather than removing them, so the tree has to be rebuilt
/// around them.
/// </summary>
[TestClass]
public class AccessibilityTreeBuildTests
{
    [TestMethod]
    public void BuildTree_FormUnderIgnoredWrappers_KeepsFormUnderMain()
    {
        // A form wrapped in five layout divs, which is what a card inside a grid inside a page
        // shell looks like by the time it reaches the browser.
        var nodes = new[]
        {
            Node("1", "RootWebArea", childIds: ["2"]),
            Node("2", "main", parentId: "1", childIds: ["w1"], backendNodeId: 20),
            Node("w1", "generic", ignored: true, parentId: "2", childIds: ["w2"]),
            Node("w2", "generic", ignored: true, parentId: "w1", childIds: ["w3"]),
            Node("w3", "generic", ignored: true, parentId: "w2", childIds: ["w4"]),
            Node("w4", "generic", ignored: true, parentId: "w3", childIds: ["w5"]),
            Node("w5", "generic", ignored: true, parentId: "w4", childIds: ["f"]),
            Node("f", "form", parentId: "w5", childIds: ["t"], backendNodeId: 30),
            Node("t", "textbox", parentId: "f", name: "Email", backendNodeId: 31),
        };

        var result = AccessibilityTreeQuery.BuildTree(nodes);

        var root = result.Roots.Single();
        Assert.AreEqual("RootWebArea", root.Role);
        var main = root.Children.Single();
        Assert.AreEqual("main", main.Role);
        var form = main.Children.Single();
        Assert.AreEqual("form", form.Role);
        Assert.AreEqual("Email", form.Children.Single().Name);
        Assert.AreEqual(5, result.IgnoredCount);
    }

    [TestMethod]
    public void BuildTree_ListItemTextUnderIgnoredDivs_StaysInsideTheListItem()
    {
        var nodes = new[]
        {
            Node("1", "RootWebArea", childIds: ["L"]),
            Node("L", "list", parentId: "1", childIds: ["li"], backendNodeId: 10),
            Node("li", "listitem", parentId: "L", childIds: ["m", "d1"], backendNodeId: 11),
            Node("m", "ListMarker", parentId: "li", name: "• ", backendNodeId: 12),
            Node("d1", "generic", ignored: true, parentId: "li", childIds: ["d2"]),
            Node("d2", "generic", ignored: true, parentId: "d1", childIds: ["s"]),
            Node("s", "StaticText", parentId: "d2", name: "Item one", backendNodeId: 13),
        };

        var result = AccessibilityTreeQuery.BuildTree(nodes);

        var list = result.Roots.Single().Children.Single();
        var item = list.Children.Single();
        Assert.AreEqual("listitem", item.Role);
        Assert.AreEqual(2, item.Children.Count);
        Assert.AreEqual("ListMarker", item.Children[0].Role);
        Assert.AreEqual("Item one", item.Children[1].Name);
    }

    [TestMethod]
    public void BuildTree_LandmarksUnderRoot_AreChildrenNotSiblings()
    {
        var nodes = new[]
        {
            Node("1", "RootWebArea", name: "MCP Bench", childIds: ["2", "3"]),
            Node("2", "banner", parentId: "1", backendNodeId: 20),
            Node("3", "main", parentId: "1", backendNodeId: 30),
        };

        var result = AccessibilityTreeQuery.BuildTree(nodes);

        var root = result.Roots.Single();
        CollectionAssert.AreEqual(
            new[] { "banner", "main" },
            root.Children.Select(c => c.Role).ToArray());
    }

    [TestMethod]
    public void BuildTree_IgnoredNodeAtTheTop_PromotesItsDescendantsToRoots()
    {
        var nodes = new[]
        {
            Node("w", "generic", ignored: true, childIds: ["a", "b"]),
            Node("a", "button", parentId: "w", name: "First", backendNodeId: 20),
            Node("b", "button", parentId: "w", name: "Second", backendNodeId: 21),
        };

        var result = AccessibilityTreeQuery.BuildTree(nodes);

        CollectionAssert.AreEqual(
            new[] { "First", "Second" },
            result.Roots.Select(r => r.Name).ToArray());
        Assert.AreEqual(1, result.IgnoredCount);
    }

    [TestMethod]
    public void BuildTree_ChildrenAllIgnoredAndChildless_LeavesTheParentEmpty()
    {
        var nodes = new[]
        {
            Node("1", "RootWebArea", childIds: ["b"]),
            Node("b", "button", parentId: "1", name: "Submit", backendNodeId: 20, childIds: ["i1", "i2"]),
            Node("i1", "generic", ignored: true, parentId: "b"),
            Node("i2", "generic", ignored: true, parentId: "b"),
        };

        var result = AccessibilityTreeQuery.BuildTree(nodes);

        var button = result.Roots.Single().Children.Single();
        Assert.AreEqual(0, button.Children.Count);
        Assert.AreEqual(2, result.IgnoredCount);
        Assert.AreEqual(2, result.AllWalkableNodes.Count, "an ignored leaf is not walkable");
    }

    [TestMethod]
    public void BuildTree_AllWalkableNodes_IsDepthFirst()
    {
        var nodes = new[]
        {
            Node("1", "RootWebArea", childIds: ["2", "5"]),
            Node("2", "main", parentId: "1", childIds: ["w"], backendNodeId: 20),
            Node("w", "generic", ignored: true, parentId: "2", childIds: ["3"]),
            Node("3", "form", parentId: "w", childIds: ["4"], backendNodeId: 30),
            Node("4", "button", parentId: "3", name: "Submit", backendNodeId: 31),
            Node("5", "contentinfo", parentId: "1", backendNodeId: 50),
        };

        var result = AccessibilityTreeQuery.BuildTree(nodes);

        CollectionAssert.AreEqual(
            new[] { "RootWebArea", "main", "form", "button", "contentinfo" },
            result.AllWalkableNodes.Select(n => n.Role).ToArray());
    }

    [TestMethod]
    [Timeout(10000)]
    public void BuildTree_CyclicReferences_Terminate()
    {
        // The browser does not send a tree that points back at itself. The guard is here so that
        // a malformed payload is a wrong answer rather than a walk that never returns.
        var nodes = new[]
        {
            Node("w1", "generic", ignored: true, parentId: "w2", childIds: ["w2"]),
            Node("w2", "generic", ignored: true, parentId: "w1", childIds: ["w1", "b"]),
            Node("b", "button", parentId: "w2", name: "Go", backendNodeId: 20),
        };

        var result = AccessibilityTreeQuery.BuildTree(nodes);

        Assert.AreEqual("Go", result.Roots.Single().Name);
    }

    [TestMethod]
    public void BuildTree_NodeItsParentDoesNotClaim_IsStillReachable()
    {
        // The protocol keeps parentId and childIds consistent. If one ever disagreed, the node
        // would still have to appear somewhere: an audit reads the flat list, and a rule that
        // stops firing is harder to notice than one that reports in an odd place.
        var nodes = new[]
        {
            Node("1", "RootWebArea"),
            Node("b", "button", parentId: "1", name: "Orphan", backendNodeId: 20),
        };

        var result = AccessibilityTreeQuery.BuildTree(nodes);

        Assert.AreEqual(2, result.AllWalkableNodes.Count);
        Assert.IsTrue(result.AllWalkableNodes.Any(n => n.Name == "Orphan"));
    }

    private static AccessibilityAXNode Node(
        string id,
        string role,
        bool ignored = false,
        string? parentId = null,
        string[]? childIds = null,
        string? name = null,
        long? backendNodeId = null)
        => new(
            NodeId: id,
            Ignored: ignored,
            Role: StringValue(role),
            Name: name is null ? null : StringValue(name),
            ChildIds: childIds,
            BackendDOMNodeId: backendNodeId,
            ParentId: parentId);

    private static AccessibilityAXValue StringValue(string text)
        => new("string", JsonDocument.Parse(
            "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"").RootElement.Clone());
}
