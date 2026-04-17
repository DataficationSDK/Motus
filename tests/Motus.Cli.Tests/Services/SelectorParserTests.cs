using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class SelectorParserTests
{
    private const string SourceFile = "/tmp/Sample.cs";

    [TestMethod]
    public void ParseSource_LocatorWithStringLiteral_ExtractsSelector()
    {
        const string source = """
            class T {
                void M() {
                    page.Locator("#submit");
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(1, result.Selectors.Count);
        Assert.AreEqual(0, result.Warnings.Count);
        var sel = result.Selectors[0];
        Assert.AreEqual("#submit", sel.Selector);
        Assert.AreEqual("Locator", sel.LocatorMethod);
        Assert.AreEqual(SourceFile, sel.SourceFile);
        Assert.AreEqual(3, sel.SourceLine);
        Assert.IsFalse(sel.IsInterpolated);
    }

    [TestMethod]
    public void ParseSource_AllLocatorMethods_ExtractsEachMethod()
    {
        const string source = """
            class T {
                void M() {
                    page.Locator("#a");
                    page.GetByRole(AriaRole.Button);
                    page.GetByText("Sign in");
                    page.GetByTestId("submit");
                    page.GetByLabel("Email");
                    page.GetByPlaceholder("you@example.com");
                    page.GetByAltText("Logo");
                    page.GetByTitle("Help");
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(8, result.Selectors.Count, "expected one entry per locator method");
        Assert.AreEqual(0, result.Warnings.Count);

        var byMethod = result.Selectors.ToDictionary(s => s.LocatorMethod);
        Assert.AreEqual("#a", byMethod["Locator"].Selector);
        Assert.AreEqual("AriaRole.Button", byMethod["GetByRole"].Selector);
        Assert.AreEqual("Sign in", byMethod["GetByText"].Selector);
        Assert.AreEqual("submit", byMethod["GetByTestId"].Selector);
        Assert.AreEqual("Email", byMethod["GetByLabel"].Selector);
        Assert.AreEqual("you@example.com", byMethod["GetByPlaceholder"].Selector);
        Assert.AreEqual("Logo", byMethod["GetByAltText"].Selector);
        Assert.AreEqual("Help", byMethod["GetByTitle"].Selector);

        Assert.AreEqual(3, byMethod["Locator"].SourceLine);
        Assert.AreEqual(10, byMethod["GetByTitle"].SourceLine);
    }

    [TestMethod]
    public void ParseSource_NameofArgument_ExtractsIdentifierValue()
    {
        const string source = """
            class T {
                void M() {
                    page.GetByTestId(nameof(LoginForm));
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(1, result.Selectors.Count);
        Assert.AreEqual(0, result.Warnings.Count);
        Assert.AreEqual("LoginForm", result.Selectors[0].Selector);
        Assert.AreEqual("GetByTestId", result.Selectors[0].LocatorMethod);
        Assert.IsFalse(result.Selectors[0].IsInterpolated);
    }

    [TestMethod]
    public void ParseSource_NameofWithMemberAccess_ExtractsLastSegment()
    {
        const string source = """
            class T {
                void M() {
                    page.GetByTestId(nameof(Forms.Login));
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(1, result.Selectors.Count);
        Assert.AreEqual("Login", result.Selectors[0].Selector);
    }

    [TestMethod]
    public void ParseSource_GetByRoleWithEnum_RecordsRoleExpression()
    {
        const string source = """
            class T {
                void M() {
                    page.GetByRole(AriaRole.Button);
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(1, result.Selectors.Count);
        Assert.AreEqual(0, result.Warnings.Count);
        Assert.AreEqual("AriaRole.Button", result.Selectors[0].Selector);
        Assert.AreEqual("GetByRole", result.Selectors[0].LocatorMethod);
        Assert.IsFalse(result.Selectors[0].IsInterpolated);
    }

    [TestMethod]
    public void ParseSource_InterpolatedString_FlagsInterpolated()
    {
        const string source = """
            class T {
                void M() {
                    var id = 42;
                    page.Locator($"#user-{id}");
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(1, result.Selectors.Count);
        Assert.AreEqual(1, result.Warnings.Count);
        Assert.IsTrue(result.Selectors[0].IsInterpolated);
        Assert.AreEqual("#user-{…}", result.Selectors[0].Selector);
        Assert.AreEqual(4, result.Warnings[0].SourceLine);
        StringAssert.Contains(result.Warnings[0].Message, "interpolated string");
    }

    [TestMethod]
    public void ParseSource_NonLocatorMethodCalls_Ignored()
    {
        const string source = """
            class T {
                void M() {
                    Console.WriteLine("hello");
                    other.SomeMethod("foo");
                    helper.Click();
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(0, result.Selectors.Count);
        Assert.AreEqual(0, result.Warnings.Count);
    }

    [TestMethod]
    public void ParseSource_DynamicArgument_EmitsWarningAndSkips()
    {
        const string source = """
            class T {
                void M() {
                    var sel = "#submit";
                    page.Locator(sel);
                    page.Locator("#a" + "#b");
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(0, result.Selectors.Count);
        Assert.AreEqual(2, result.Warnings.Count);
        StringAssert.Contains(result.Warnings[0].Message, "not a static expression");
    }

    [TestMethod]
    public void ParseSource_MultipleSelectorsInOneFile_ReportsCorrectLineNumbers()
    {
        const string source = """
            class T {
                void M() {
                    page.Locator("#a");
                    page.Locator("#b");

                    page.Locator("#c");
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(3, result.Selectors.Count);
        Assert.AreEqual(3, result.Selectors[0].SourceLine);
        Assert.AreEqual(4, result.Selectors[1].SourceLine);
        Assert.AreEqual(6, result.Selectors[2].SourceLine);
    }

    [TestMethod]
    public void ParseSource_EmptyArgumentList_DoesNotThrow()
    {
        // .Locator() with no args is invalid C#, but Roslyn parses it anyway.
        // The parser must not crash; it should silently skip.
        const string source = """
            class T {
                void M() {
                    page.Locator();
                }
            }
            """;

        var result = SelectorParser.ParseSource(source, SourceFile);

        Assert.AreEqual(0, result.Selectors.Count);
        Assert.AreEqual(0, result.Warnings.Count);
    }

    [TestMethod]
    public async Task ParseGlobAsync_ResolvesGlobAndAggregates()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"motus-parser-test-{Guid.NewGuid():N}");
        var nestedDir = Path.Combine(tempDir, "nested");
        Directory.CreateDirectory(nestedDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "A.cs"),
                """
                class A { void M() { page.Locator("#a"); } }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(nestedDir, "B.cs"),
                """
                class B { void M() { page.GetByText("hello"); } }
                """);
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "Notes.txt"),
                "page.Locator(\"#ignored\")");

            var result = await SelectorParser.ParseGlobAsync("**/*.cs", tempDir);

            Assert.AreEqual(2, result.Selectors.Count);
            var selectorValues = result.Selectors.Select(s => s.Selector).OrderBy(s => s).ToArray();
            CollectionAssert.AreEqual(new[] { "#a", "hello" }, selectorValues);

            foreach (var sel in result.Selectors)
                Assert.IsTrue(Path.IsPathRooted(sel.SourceFile), "SourceFile should be absolute");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task ParseGlobAsync_NoMatches_ReturnsEmpty()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"motus-parser-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var result = await SelectorParser.ParseGlobAsync("**/*.cs", tempDir);

            Assert.AreEqual(0, result.Selectors.Count);
            Assert.AreEqual(0, result.Warnings.Count);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task ParseGlobAsync_NonexistentBaseDirectory_ReturnsEmpty()
    {
        var bogus = Path.Combine(Path.GetTempPath(), $"motus-parser-missing-{Guid.NewGuid():N}");

        var result = await SelectorParser.ParseGlobAsync("**/*.cs", bogus);

        Assert.AreEqual(0, result.Selectors.Count);
        Assert.AreEqual(0, result.Warnings.Count);
    }
}
