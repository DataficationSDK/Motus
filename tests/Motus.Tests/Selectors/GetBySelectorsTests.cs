namespace Motus.Tests.Selectors;

/// <summary>
/// Pins the selectors the <c>GetBy*</c> factories build, and that each one parses back to the
/// text it was built from whatever characters that text holds.
/// </summary>
[TestClass]
public class GetBySelectorsTests
{
    private const string Awkward = """Say "hi" [now] \ 'ok' ]"]""";

    [TestMethod]
    public void Role_GoesThroughTheRoleEngine()
    {
        Assert.AreEqual("role=button", GetBySelectors.Role("button", null));
        Assert.AreEqual("""role=button[name="Save"]""", GetBySelectors.Role("button", "Save"));
        Assert.AreEqual("role", Motus.Locator.ParsePrefix(GetBySelectors.Role("button", "Save")).prefix);
    }

    [TestMethod]
    public void Role_NameRoundTripsThroughTheParser()
    {
        foreach (var name in new[] { Awkward, "", "a]b", "ends with quote\"", "back\\slash\\" })
        {
            var (prefix, expression) = Motus.Locator.ParsePrefix(GetBySelectors.Role("button", name));
            Assert.AreEqual("role", prefix);

            var (role, parsed) = RoleSelectorStrategy.ParseRoleSelector(expression);
            Assert.AreEqual("button", role);
            Assert.AreEqual(name, parsed);
        }
    }

    [TestMethod]
    public void RoleQuery_TriesBothSpellingsOfTheImageRole()
    {
        CollectionAssert.AreEqual(new[] { "image", "img" }, RoleSelectorStrategy.QueryRoles("img").ToArray());
        CollectionAssert.AreEqual(new[] { "image", "img" }, RoleSelectorStrategy.QueryRoles("image").ToArray());
        CollectionAssert.AreEqual(new[] { "button" }, RoleSelectorStrategy.QueryRoles("button").ToArray());
    }

    [TestMethod]
    public void TextFactories_DefaultToALooseMatch()
    {
        Assert.AreEqual("_text=\"Save\"i", GetBySelectors.Text("Save", null));
        Assert.AreEqual("_text=\"Save\"i", GetBySelectors.Text("Save", false));
        Assert.AreEqual("_text=\"Save\"s", GetBySelectors.Text("Save", true));
        Assert.AreEqual("_label=\"Email\"i", GetBySelectors.Label("Email", null));
        Assert.AreEqual("_attr=placeholder=\"Search\"s", GetBySelectors.Placeholder("Search", true));
        Assert.AreEqual("_attr=title=\"Close\"i", GetBySelectors.Title("Close", null));
        Assert.AreEqual("_attr=alt=\"Logo\"i", GetBySelectors.AltText("Logo", null));
    }

    [TestMethod]
    public void TextFactories_RoundTripAnyText()
    {
        foreach (var text in new[] { Awkward, "", "Café", "line\nbreak", "tab\there", "\u2028" })
        {
            foreach (var exact in new[] { true, false })
            {
                var (prefix, expression) = Motus.Locator.ParsePrefix(GetBySelectors.Label(text, exact));
                Assert.AreEqual("_label", prefix);
                var (attribute, parsed, parsedExact) = GetBySelectorStrategy.ParseExpression(expression, hasAttribute: false);
                Assert.IsNull(attribute);
                Assert.AreEqual(text, parsed);
                Assert.AreEqual(exact, parsedExact);

                (prefix, expression) = Motus.Locator.ParsePrefix(GetBySelectors.Placeholder(text, exact));
                Assert.AreEqual("_attr", prefix);
                (attribute, parsed, parsedExact) = GetBySelectorStrategy.ParseExpression(expression, hasAttribute: true);
                Assert.AreEqual("placeholder", attribute);
                Assert.AreEqual(text, parsed);
                Assert.AreEqual(exact, parsedExact);
            }
        }
    }

    [TestMethod]
    public void ParseExpression_RejectsAMalformedExpression()
    {
        foreach (var bad in new[] { "Save", "\"Save\"", "\"Save\"x", "\"Sa\"ve\"i", "i", "" })
        {
            Assert.ThrowsException<InvalidOperationException>(
                () => GetBySelectorStrategy.ParseExpression(bad, hasAttribute: false), bad);
        }

        Assert.ThrowsException<InvalidOperationException>(
            () => GetBySelectorStrategy.ParseExpression("\"Save\"i", hasAttribute: true));
    }

    [TestMethod]
    public void TestId_QuotesTheValueAsACssString()
    {
        Assert.AreEqual("[data-testid=\"go\"]", GetBySelectors.TestId("go"));
        Assert.AreEqual("[data-testid=\"a\\\"b\\\\c]\"]", GetBySelectors.TestId("a\"b\\c]"));
        Assert.AreEqual("[data-testid=\"a\\a b\"]", GetBySelectors.TestId("a\nb"));
    }
}
