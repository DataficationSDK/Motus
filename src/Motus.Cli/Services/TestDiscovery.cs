using System.Reflection;
using Motus.Runner.Services.Execution;

namespace Motus.Cli.Services;

/// <summary>
/// A test case found in a test assembly. <paramref name="Arguments"/> is the data row of one case of a
/// data-driven test, or null for a test without one.
/// </summary>
public sealed record DiscoveredTest(Type TestClass, MethodInfo TestMethod, string FullName, bool IsIgnored, bool Quarantined = false, object?[]? Arguments = null);

public sealed class TestDiscovery
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

    public List<DiscoveredTest> Discover(string[] assemblyPaths, string? filter)
    {
        var tests = new List<DiscoveredTest>();

        foreach (var path in assemblyPaths)
        {
            var fullPath = Path.GetFullPath(path);
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(fullPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to load assembly {fullPath}: {ex.Message}");
                continue;
            }

            foreach (var type in assembly.GetExportedTypes())
            {
                if (!IsTestClass(type))
                    continue;

                var classIgnored = type.GetCustomAttributes(true)
                    .Any(a => IgnoreAttributes.Contains(a.GetType().Name));

                var classQuarantined = type.GetCustomAttributes(true)
                    .Any(a => QuarantineAttributes.Contains(a.GetType().Name));

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!IsTestMethod(method))
                        continue;

                    var methodIgnored = classIgnored || IsIgnoredMethod(method);
                    var quarantined = classQuarantined || IsQuarantinedMethod(method);

                    foreach (var testCase in TestCaseExpander.Expand(type, method, message => Console.Error.WriteLine(message)))
                    {
                        if (filter is not null && !testCase.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var isIgnored = methodIgnored || testCase.IgnoreReason is not null;
                        tests.Add(new DiscoveredTest(type, method, testCase.FullName, isIgnored, quarantined, testCase.Arguments));
                    }
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

    private static bool IsIgnoredMethod(MethodInfo method)
    {
        var attrs = method.GetCustomAttributes(true);

        // MSTest [Ignore] / NUnit [Ignore]
        if (attrs.Any(a => IgnoreAttributes.Contains(a.GetType().Name)))
            return true;

        // xUnit [Fact(Skip = "reason")] -- check for non-null Skip property on FactAttribute
        var factAttr = attrs.FirstOrDefault(a => a.GetType().Name == "FactAttribute");
        if (factAttr is not null)
        {
            var skipProp = factAttr.GetType().GetProperty("Skip");
            if (skipProp?.GetValue(factAttr) is string)
                return true;
        }

        return false;
    }

    private static bool IsQuarantinedMethod(MethodInfo method)
    {
        return method.GetCustomAttributes(true)
            .Any(a => QuarantineAttributes.Contains(a.GetType().Name));
    }
}
