using System.Reflection;
using Motus.Runner.Services.Models;

namespace Motus.Runner.Services;

public sealed class TestDiscovery(ILogger<TestDiscovery> logger)
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
                logger.LogError("Failed to load assembly {Path}: {Message}", fullPath, ex.Message);
                continue;
            }

            var assemblyName = assembly.GetName().Name ?? Path.GetFileNameWithoutExtension(fullPath);

            foreach (var type in assembly.GetExportedTypes())
            {
                if (!IsTestClass(type))
                    continue;

                var (classIgnored, classReason) = GetIgnoreInfo(type.GetCustomAttributes(true));

                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!IsTestMethod(method))
                        continue;

                    var fullName = $"{type.FullName}.{method.Name}";

                    if (filter is not null && !fullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var (methodIgnored, methodReason) = GetIgnoreInfo(method.GetCustomAttributes(true));
                    var isIgnored = classIgnored || methodIgnored;
                    var reason = classIgnored ? classReason : methodReason;
                    tests.Add(new DiscoveredTest(type, method, fullName, assemblyName, isIgnored, reason));
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
