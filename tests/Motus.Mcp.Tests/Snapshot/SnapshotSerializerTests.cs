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

    /// <summary>
    /// The shape the browser gives every piece of text: a static text node carrying the text,
    /// with an inline text box under it carrying the same text again.
    /// </summary>
    private static AccessibilityNode Text(string text, long backendId)
        => Node("StaticText", text, backendId, children: [Node("InlineTextBox", text, backendId: null)]);

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

        Assert.AreEqual(10, result.Refs["e1"].BackendNodeId);
        Assert.AreEqual(11, result.Refs["e2"].BackendNodeId);
        Assert.AreEqual(12, result.Refs["e3"].BackendNodeId);
        Assert.AreEqual(3, result.Refs.Count);
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
        Assert.AreEqual(1, result.Refs.Count, "Only the node with a backend id is refed.");
        Assert.AreEqual(10, result.Refs["e1"].BackendNodeId);
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
        var result = SnapshotSerializer.Serialize(Snapshot(Node(role: null, name: "Card", backendId: null)));
        StringAssert.StartsWith(result.Text, "- generic \"Card\"");
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
            first.Refs.ToList(),
            second.Refs.ToList());
    }

    // --- compaction, one rule per test ---

    [TestMethod]
    public void InlineTextBox_IsNeverPrinted()
    {
        var snapshot = Snapshot(Node("paragraph", null, backendId: 1, children:
        [
            Node("StaticText", "Hello", backendId: 2, children:
            [
                Node("InlineTextBox", "Hello", backendId: null),
                Node("InlineTextBox", "Hello", backendId: 3),
            ]),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.IsFalse(result.Text.Contains("InlineTextBox"), result.Text);
        Assert.AreEqual(0, result.Refs.Count, "layout nodes never take a ref");
    }

    [TestMethod]
    public void StaticText_ThatRepeatsTheParentName_IsFoldedAway()
    {
        var snapshot = Snapshot(Node("button", "Submit", backendId: 1, children: [Text("Submit", 2)]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- button \"Submit\" [ref=e1]\n", result.Text);
    }

    [TestMethod]
    public void StaticText_ThatDiffersFromTheParentName_IsPrintedInline()
    {
        var snapshot = Snapshot(Node("button", "Hide contents", backendId: 1, children: [Text("hide", 2)]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- button \"Hide contents\" [ref=e1]: hide\n", result.Text);
        Assert.AreEqual(1, result.Refs.Count, "text never takes a ref of its own");
    }

    [TestMethod]
    public void StaticText_SplitAcrossPieces_IsJoined_AndFoldedWhenItSpellsTheName()
    {
        var folded = SnapshotSerializer.Serialize(Snapshot(
            Node("link", "Open in new tab", backendId: 1, children: [Text("Open in ", 2), Text("new tab", 3)])));
        var kept = SnapshotSerializer.Serialize(Snapshot(
            Node("listitem", null, backendId: 1, children: [Text("Item ", 2), Text("one", 3)])));

        Assert.AreEqual("- link \"Open in new tab\" [ref=e1]\n", folded.Text);
        Assert.AreEqual("- listitem: Item one\n", kept.Text);
    }

    [TestMethod]
    public void StaticText_BesideElements_IsPrintedAsATextLine_InDocumentOrder()
    {
        var snapshot = Snapshot(Node("paragraph", null, backendId: 1, children:
        [
            Text("See ", 2),
            Node("link", "the docs", backendId: 3, children: [Text("the docs", 4)]),
            Text(" for more.", 5),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        var expected =
            "- paragraph\n" +
            "  - text: See\n" +
            "  - link \"the docs\" [ref=e1]\n" +
            "  - text: for more.\n";
        Assert.AreEqual(expected, result.Text);
    }

    [TestMethod]
    public void WhitespaceOnlyText_IsDropped()
    {
        var snapshot = Snapshot(Node("navigation", null, backendId: 1, children:
        [
            Node("link", "A", backendId: 2),
            Text("  \n  ", 3),
            Node("link", "B", backendId: 4),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- navigation\n  - link \"A\" [ref=e1]\n  - link \"B\" [ref=e2]\n", result.Text);
    }

    [TestMethod]
    public void UnnamedGeneric_WithOneChild_StepsAsideForTheChild_ThroughTheWholeChain()
    {
        // A list item's text sits under two layout divs, and the item itself carries a marker.
        var snapshot = Snapshot(Node("listitem", null, backendId: 1, children:
        [
            Node("ListMarker", "• ", backendId: 2),
            Node("generic", null, backendId: 3, children:
            [
                Node("generic", null, backendId: 4, children: [Text("Item one", 5)]),
            ]),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- listitem: Item one\n", result.Text);
        Assert.AreEqual(0, result.Refs.Count);
    }

    [TestMethod]
    public void UnnamedGeneric_WithNoChildren_IsOmitted()
    {
        // Chrome puts an empty editable wrapper inside every text field.
        var snapshot = Snapshot(Node("textbox", "Email", backendId: 1, children:
        [
            Node("generic", null, backendId: 2, properties: new Dictionary<string, string?> { ["editable"] = "plaintext" }),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- textbox \"Email\" [ref=e1]\n", result.Text);
    }

    [TestMethod]
    public void UnnamedGeneric_WithSeveralChildren_Stays_WithoutARef()
    {
        var snapshot = Snapshot(Node("generic", null, backendId: 1, children:
        [
            Node("button", "A", backendId: 2),
            Node("button", "B", backendId: 3),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- generic\n  - button \"A\" [ref=e1]\n  - button \"B\" [ref=e2]\n", result.Text);
    }

    [TestMethod]
    public void Generic_WithSomethingOfItsOwnToSay_IsNotCollapsed()
    {
        var named = SnapshotSerializer.Serialize(Snapshot(
            Node("generic", "Card", backendId: 1, children: [Node("button", "Go", backendId: 2)])));
        var focusable = SnapshotSerializer.Serialize(Snapshot(
            Node("generic", null, backendId: 1, children: [Text("Click me", 2)],
                properties: new Dictionary<string, string?> { ["focusable"] = "true" })));
        var valued = SnapshotSerializer.Serialize(Snapshot(
            Node("generic", null, backendId: 1, children: [Node("button", "Go", backendId: 2)], value: "42")));

        Assert.AreEqual("- generic \"Card\" [ref=e1]\n  - button \"Go\" [ref=e2]\n", named.Text);
        Assert.AreEqual("- generic [ref=e1]: Click me\n", focusable.Text);
        Assert.AreEqual("- generic [value=\"42\"]\n  - button \"Go\" [ref=e1]\n", valued.Text);
    }

    [TestMethod]
    public void Refs_GoToInteractiveNamedFocusableNodes_AndFrames_Only()
    {
        var snapshot = Snapshot(Node("RootWebArea", null, backendId: 1, children:
        [
            Node("button", "", backendId: 2),
            Node("img", "", backendId: 3),
            Node("img", "Logo", backendId: 4),
            Node("Iframe", "", backendId: 5),
            Node("LabelText", "", backendId: 6, children: [Text("Email", 7)]),
            Node("region", "", backendId: 8, properties: new Dictionary<string, string?> { ["focusable"] = "true" }),
            Node("paragraph", "", backendId: 9, children: [Text("Plain words", 10)]),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        var expected =
            "- RootWebArea\n" +
            "  - button [ref=e1]\n" +
            "  - img\n" +
            "  - img \"Logo\" [ref=e2]\n" +
            "  - Iframe [ref=e3]\n" +
            "  - LabelText: Email\n" +
            "  - region [ref=e4]\n" +
            "  - paragraph: Plain words\n";
        Assert.AreEqual(expected, result.Text);
        Assert.AreEqual(2, result.Refs["e1"].BackendNodeId);
        Assert.AreEqual(4, result.Refs["e2"].BackendNodeId);
        Assert.AreEqual(5, result.Refs["e3"].BackendNodeId);
        Assert.AreEqual(8, result.Refs["e4"].BackendNodeId);
    }

    [TestMethod]
    public void ListMarker_IsNeverPrinted()
    {
        var snapshot = Snapshot(Node("listitem", null, backendId: 1, children:
        [
            Node("ListMarker", "1. ", backendId: 2),
            Node("link", "First", backendId: 3),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- listitem\n  - link \"First\" [ref=e1]\n", result.Text);
    }

    [TestMethod]
    public void Heading_ShowsItsLevel_AndOtherRolesDoNot()
    {
        var level = new Dictionary<string, string?> { ["level"] = "2" };
        var snapshot = Snapshot(
            Node("heading", "History", backendId: 1, properties: level),
            Node("listitem", null, backendId: 2, children: [Text("Nested", 3)], properties: level));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- heading \"History\" [ref=e1] [level=2]\n- listitem: Nested\n", result.Text);
    }

    [TestMethod]
    public void Link_ShowsItsUrl()
    {
        var snapshot = Snapshot(Node("link", "Docs", backendId: 1,
            properties: new Dictionary<string, string?> { ["url"] = "https://example.com/docs" }));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- link \"Docs\" [ref=e1] [url=https://example.com/docs]\n", result.Text);
    }

    [TestMethod]
    public void Names_AreQuotedSafely_AndPrintedOnOneLine()
    {
        var snapshot = Snapshot(Node("button", "Say \"hi\"\n  to\tall", backendId: 1, value: "a \"b\""));

        var result = SnapshotSerializer.Serialize(snapshot);

        Assert.AreEqual("- button \"Say \\\"hi\\\" to all\" [ref=e1] [value=\"a \\\"b\\\"\"]\n", result.Text);
    }

    [TestMethod]
    public void MaxDepth_CountsPrintedLevels_NotCollapsedWrappers()
    {
        var snapshot = Snapshot(Node("main", null, backendId: 1, children:
        [
            Node("generic", null, backendId: 2, children:
            [
                Node("generic", null, backendId: 3, children:
                [
                    Node("button", "Deep", backendId: 4, children: [Text("Deep", 5)]),
                ]),
            ]),
        ]));

        var result = SnapshotSerializer.Serialize(snapshot.Roots, maxDepth: 1);

        Assert.AreEqual("- main\n  - button \"Deep\" [ref=e1]\n", result.Text);
    }

    [TestMethod]
    public void InlineText_GathersTheTextUnderANode_AndCutsItShort()
    {
        var node = Node("region", null, backendId: 1, children:
        [
            Node("generic", null, backendId: 2, children: [Text("First ", 3)]),
            Node("link", "x", backendId: 4, children: [Text("second", 5)]),
        ]);
        var long_ = Node("paragraph", null, backendId: 1, children: [Text(new string('a', 200), 2)]);

        Assert.AreEqual("First second", SnapshotSerializer.InlineText(node));
        Assert.IsNull(SnapshotSerializer.InlineText(Node("img", "", backendId: 1)));

        var cut = SnapshotSerializer.InlineText(long_)!;
        Assert.IsTrue(cut.EndsWith("...", StringComparison.Ordinal), cut);
        Assert.IsTrue(cut.Length < 100, cut);
    }
}
