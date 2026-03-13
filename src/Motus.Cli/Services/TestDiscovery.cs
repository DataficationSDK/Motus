using System.Reflection;

namespace Motus.Cli.Services;

public sealed record DiscoveredTest(Type TestClass, MethodInfo TestMethod, string FullName);

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

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!IsTestMethod(method))
                        continue;

                    var fullName = $"{type.FullName}.{method.Name}";

                    if (filter is not null && !fullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    tests.Add(new DiscoveredTest(type, method, fullName));
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
}
