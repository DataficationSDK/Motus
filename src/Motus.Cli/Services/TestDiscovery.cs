using System.Reflection;

namespace Motus.Cli.Services;

public sealed record DiscoveredTest(Type TestClass, MethodInfo TestMethod, string FullName, bool IsIgnored);

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

    private static readonly HashSet<string> IgnoreAttributes = new(StringComparer.Ordinal)
    {
        "IgnoreAttribute",  // MSTest + NUnit
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

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!IsTestMethod(method))
                        continue;

                    var fullName = $"{type.FullName}.{method.Name}";

                    if (filter is not null && !fullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var isIgnored = classIgnored || IsIgnoredMethod(method);
                    tests.Add(new DiscoveredTest(type, method, fullName, isIgnored));
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
            .Any(a => ClassAttributes.Contains(a.GetType().Name));
    }

    private static bool IsTestMethod(MethodInfo method)
    {
        return method.GetCustomAttributes(true)
            .Any(a => MethodAttributes.Contains(a.GetType().Name));
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
}
