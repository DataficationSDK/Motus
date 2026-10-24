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
/// <param name="Id">Identifies the test case without depending on its display name: the class, the
/// method with its parameter types, and for a data row, which data source on the method and which
/// row of it. A data source can name its rows differently each time it is read (a Guid or a time in
/// the row), so this is what a runner that discovers again in a new process looks the case up by.</param>
/// <param name="ConstantName">True when the name is the same on every read: a test without data
/// rows, or a <c>[DataRow]</c>, whose values are constants. Such a case is found by its name only.</param>
internal sealed record ExpandedTestCase(string FullName, object?[]? Arguments, string? IgnoreReason, string Id, bool ConstantName = true);

/// <summary>
/// Turns a test method into its test cases, one per <c>[DataRow]</c>, <c>[DynamicData]</c> row or
/// other MSTest <c>ITestDataSource</c> row, the way MSTest does. Shared by both runners' discovery.
/// </summary>
internal static class TestCaseExpander
{
    private const string TestDataSourceInterfaceName = "Microsoft.VisualStudio.TestTools.UnitTesting.ITestDataSource";

    private static readonly HashSet<string> DataRowAttributeNames = new(StringComparer.Ordinal) { "DataRowAttribute" };

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
        var methodId = MethodId(testClass, method);
        var cases = new List<ExpandedTestCase>();
        var sourceIndex = -1;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attribute in method.GetCustomAttributes(true))
        {
            var dataSource = attribute.GetType().GetInterfaces().FirstOrDefault(i => i.FullName == TestDataSourceInterfaceName);
            var constantRows = IsOrDerivesFrom(attribute, DataRowAttributeNames);
            if (dataSource is null)
                continue;

            sourceIndex++;

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

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var row = rows[rowIndex];
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

                cases.Add(new ExpandedTestCase(unique, row, ignoreReason, RowId(methodId, sourceIndex, rowIndex), constantRows));
            }
        }

        // No data source, or none that could be read: a single case. One whose method takes
        // parameters fails when run, saying they were not supplied.
        if (cases.Count == 0)
            cases.Add(new ExpandedTestCase(baseName, null, null, methodId));

        return cases;
    }

    /// <summary>
    /// <c>{Class}.{Method}({parameter types})</c>, which tells overloads apart where the full name does not.
    /// </summary>
    internal static string MethodId(Type testClass, MethodInfo method)
        => $"{testClass.FullName}.{method.Name}({string.Join(",", method.GetParameters().Select(p => p.ParameterType.FullName ?? p.ParameterType.Name))})";

    /// <summary>The id of row <paramref name="rowIndex"/> of the method's data source <paramref name="sourceIndex"/>.</summary>
    internal static string RowId(string methodId, int sourceIndex, int rowIndex) => $"{methodId}#{sourceIndex}:{rowIndex}";

    /// <summary>Splits a row id into its method id, data source and row, or returns false for a test without rows.</summary>
    internal static bool TryParseRowId(string id, out string methodId, out int sourceIndex, out int rowIndex)
    {
        methodId = id;
        sourceIndex = rowIndex = -1;
        var hash = id.LastIndexOf('#');
        if (hash < 0 || id.IndexOf(')', hash) >= 0)
            return false;

        var parts = id[(hash + 1)..].Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out sourceIndex) || !int.TryParse(parts[1], out rowIndex))
            return false;

        methodId = id[..hash];
        return true;
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
