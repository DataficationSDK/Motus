using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// One test case found by discovery: a test method, or one data row of a data-driven one.
/// </summary>
/// <param name="FullName">The name the runners report and filter on.</param>
/// <param name="Arguments">The data row, or null for a test method without one.</param>
/// <param name="IgnoreReason">Set when the data row itself is marked ignored.</param>
internal sealed record ExpandedTestCase(string FullName, object?[]? Arguments, string? IgnoreReason);

/// <summary>
/// Turns a test method into its test cases, one per <c>[DataRow]</c>, <c>[DynamicData]</c> row or
/// other MSTest <c>ITestDataSource</c> row, the way MSTest does. Shared by both runners' discovery.
/// </summary>
internal static class TestCaseExpander
{
    private const string TestDataSourceInterfaceName = "Microsoft.VisualStudio.TestTools.UnitTesting.ITestDataSource";

    /// <summary>
    /// True when <paramref name="attribute"/>'s type, or a type it derives from, is named in
    /// <paramref name="names"/>, so that <c>[DataTestMethod]</c> and other subclasses of
    /// <c>[TestMethod]</c> or <c>[TestClass]</c> count as what they extend.
    /// </summary>
    public static bool IsOrDerivesFrom(object attribute, IReadOnlySet<string> names)
    {
        for (var type = attribute.GetType(); type is not null && type != typeof(Attribute); type = type.BaseType)
        {
            if (names.Contains(type.Name))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The test cases of <paramref name="method"/> on <paramref name="testClass"/>: a single case
    /// named <c>{Class}.{Method}</c> for a method without data rows, or one per row named
    /// <c>{Class}.{display name}</c>, where the display name is MSTest's own, such as
    /// <c>SignsIn ("owner")</c>. <paramref name="onError"/> is told about a data source that
    /// could not be read.
    /// </summary>
    public static List<ExpandedTestCase> Expand(Type testClass, MethodInfo method, Action<string>? onError = null)
    {
        var baseName = $"{testClass.FullName}.{method.Name}";
        var cases = new List<ExpandedTestCase>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in method.GetCustomAttributes(true))
        {
            var dataSource = attribute.GetType().GetInterfaces().FirstOrDefault(i => i.FullName == TestDataSourceInterfaceName);
            if (dataSource is null)
                continue;

            List<object?[]> rows;
            try
            {
                var data = dataSource.GetMethod("GetData")!.Invoke(attribute, [method]) as IEnumerable;
                rows = data?.Cast<object?[]>().ToList() ?? [];
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException { InnerException: { } e } ? e : ex;
                onError?.Invoke($"Could not read the test data for {baseName} from [{attribute.GetType().Name}]: {inner.Message}");
                continue;
            }

            // MSTest 3.8 and later can ignore a single row.
            var ignoreReason = attribute.GetType().GetProperty("IgnoreMessage")?.GetValue(attribute) as string;
            var getDisplayName = dataSource.GetMethod("GetDisplayName");

            foreach (var row in rows)
            {
                var displayName = TryGetDisplayName(getDisplayName, attribute, method, row)
                    ?? DefaultDisplayName(method, row);

                // A display name that drops the method name still gets it, so --filter by method works.
                var fullName = displayName.StartsWith(method.Name, StringComparison.Ordinal)
                    ? $"{testClass.FullName}.{displayName}"
                    : $"{baseName} ({displayName})";

                // Two rows with the same values would otherwise share a name, and the runners key results by it.
                var unique = fullName;
                for (var n = 2; !seen.Add(unique); n++)
                    unique = $"{fullName} #{n}";

                cases.Add(new ExpandedTestCase(unique, row, ignoreReason));
            }
        }

        // No data source, or none that could be read: a single case. One whose method takes
        // parameters fails when run, saying they were not supplied.
        if (cases.Count == 0)
            cases.Add(new ExpandedTestCase(baseName, null, null));

        return cases;
    }

    private static string? TryGetDisplayName(MethodInfo? getDisplayName, object attribute, MethodInfo method, object?[] row)
    {
        if (getDisplayName is null)
            return null;

        try
        {
            var name = getDisplayName.Invoke(attribute, [method, row]) as string;
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }

    private static string DefaultDisplayName(MethodInfo method, object?[] row)
        => $"{method.Name} ({string.Join(", ", row.Select(FormatValue))})";

    private static string FormatValue(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        char c => $"'{c}'",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        Array a => $"[{string.Join(", ", a.Cast<object?>().Select(FormatValue))}]",
        _ => value.ToString() ?? value.GetType().Name,
    };
}
