using System.Reflection;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// A test case found in a test assembly loaded into this process, with what it takes to run it.
/// </summary>
/// <param name="FullName">The name the runners report and filter on.</param>
/// <param name="Arguments">The data row, or null for a test method without one.</param>
/// <param name="IgnoreReason">Why the test is ignored, when it is and a reason was given.</param>
/// <param name="Quarantined">True when the test or its class carries <c>[Quarantine]</c>.</param>
/// <param name="Id">The case's identity apart from its name (see <see cref="ExpandedTestCase.Id"/>).
/// Defaults to <paramref name="FullName"/> for a case built by hand.</param>
internal sealed record DiscoveredTestCase(
    Type TestClass,
    MethodInfo TestMethod,
    string FullName,
    object?[]? Arguments,
    bool IsIgnored,
    string? IgnoreReason,
    bool Quarantined,
    string? Id = null)
{
    /// <summary>The case's identity, which the runner asks for it by.</summary>
    public string Id { get; init; } = Id ?? FullName;

    /// <summary>What a runner in another process is told about this test case.</summary>
    public TestCaseInfo ToInfo()
        => new(FullName, TestClass.FullName ?? TestClass.Name, TestMethod.Name, IsIgnored, IgnoreReason, Quarantined, Id);
}

/// <summary>
/// Finds the tests in a loaded test assembly: MSTest, NUnit and xUnit test classes and methods,
/// expanded into one test case per data row. Runs in the test host process, for both runners.
/// </summary>
internal static class TestCaseDiscovery
{
    private static readonly HashSet<string> ClassAttributes = new(StringComparer.Ordinal)
    {
        "TestClassAttribute",
        "TestFixtureAttribute",
    };

    private static readonly HashSet<string> MethodAttributes = new(StringComparer.Ordinal)
    {
        "TestMethodAttribute",
        "TestAttribute",
        "FactAttribute",
    };

    // xUnit's [Theory] derives from [Fact] but needs data the runners don't supply.
    private static readonly HashSet<string> ExcludedMethodAttributes = new(StringComparer.Ordinal)
    {
        "TheoryAttribute",
    };

    private static readonly HashSet<string> IgnoreAttributes = new(StringComparer.Ordinal)
    {
        "IgnoreAttribute",  // MSTest + NUnit
    };

    private static readonly HashSet<string> QuarantineAttributes = new(StringComparer.Ordinal)
    {
        "QuarantineAttribute",
    };

    /// <summary>
    /// The test cases in <paramref name="assembly"/> whose full name contains <paramref name="filter"/>
    /// (ignoring case), or all of them when it is null. <paramref name="onWarning"/> is told about a
    /// data source that could not be read.
    /// </summary>
    public static List<DiscoveredTestCase> Discover(Assembly assembly, string? filter, Action<string>? onWarning = null)
    {
        var tests = new List<DiscoveredTestCase>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (!IsTestClass(type))
                continue;

            var classAttributes = type.GetCustomAttributes(true);
            var (classIgnored, classReason) = GetIgnoreInfo(classAttributes);
            var classQuarantined = classAttributes.Any(a => QuarantineAttributes.Contains(a.GetType().Name));

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!IsTestMethod(method))
                    continue;

                var methodAttributes = method.GetCustomAttributes(true);
                var (methodIgnored, methodReason) = GetIgnoreInfo(methodAttributes);
                var quarantined = classQuarantined || methodAttributes.Any(a => QuarantineAttributes.Contains(a.GetType().Name));

                foreach (var testCase in TestCaseExpander.Expand(type, method, onWarning))
                {
                    if (filter is not null && !testCase.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var isIgnored = classIgnored || methodIgnored || testCase.IgnoreReason is not null;
                    var reason = classIgnored ? classReason : methodIgnored ? methodReason : testCase.IgnoreReason;
                    tests.Add(new DiscoveredTestCase(type, method, testCase.FullName, testCase.Arguments, isIgnored, reason, quarantined, testCase.Id));
                }
            }
        }

        return tests;
    }

    private static bool IsTestClass(Type type)
    {
        if (type.IsAbstract || !type.IsClass)
            return false;

        return type.GetCustomAttributes(true)
            .Any(a => TestCaseExpander.IsOrDerivesFrom(a, ClassAttributes));
    }

    private static bool IsTestMethod(MethodInfo method)
    {
        return method.GetCustomAttributes(true)
            .Any(a => TestCaseExpander.IsOrDerivesFrom(a, MethodAttributes)
                      && !TestCaseExpander.IsOrDerivesFrom(a, ExcludedMethodAttributes));
    }

    private static (bool IsIgnored, string? Reason) GetIgnoreInfo(object[] attrs)
    {
        // MSTest [Ignore] / [Ignore("reason")] / NUnit [Ignore("reason")]
        var ignoreAttr = attrs.FirstOrDefault(a => IgnoreAttributes.Contains(a.GetType().Name));
        if (ignoreAttr is not null)
        {
            // MSTest uses IgnoreMessage; NUnit uses Reason
            var msg = ignoreAttr.GetType().GetProperty("IgnoreMessage")?.GetValue(ignoreAttr) as string
                   ?? ignoreAttr.GetType().GetProperty("Reason")?.GetValue(ignoreAttr) as string;
            return (true, msg);
        }

        // xUnit [Fact(Skip = "reason")]
        var factAttr = attrs.FirstOrDefault(a => a.GetType().Name == "FactAttribute");
        if (factAttr is not null)
        {
            var skip = factAttr.GetType().GetProperty("Skip")?.GetValue(factAttr) as string;
            if (skip is not null)
                return (true, skip);
        }

        return (false, null);
    }
}
