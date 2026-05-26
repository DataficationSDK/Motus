using System.Globalization;
using System.Xml.Linq;
using Motus.Runner.Services.Models;

namespace Motus.Runner.Services;

public sealed record TrxParseResult(
    IReadOnlyList<DiscoveredTest> Tests,
    IReadOnlyDictionary<string, TestNodeState> States,
    TrxSummary Summary);

public sealed record TrxSummary(
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    TimeSpan Duration,
    string? RunName,
    string FileName);

public sealed class TrxParserService
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    public TrxParseResult ParseFile(string path)
    {
        var doc = XDocument.Load(path);
        var root = doc.Root ?? throw new InvalidOperationException("TRX file has no root element");

        var fileName = Path.GetFileName(path);
        var runName = root.Attribute("name")?.Value;

        // Index definitions by testId so we can join with results.
        var definitions = new Dictionary<string, TrxDefinition>(StringComparer.Ordinal);
        var definitionsByName = new Dictionary<string, TrxDefinition>(StringComparer.Ordinal);
        foreach (var unitTest in root.Elements(Ns + "TestDefinitions").Elements(Ns + "UnitTest"))
        {
            var id = unitTest.Attribute("id")?.Value;
            var name = unitTest.Attribute("name")?.Value ?? "";
            var testMethod = unitTest.Element(Ns + "TestMethod");
            var className = testMethod?.Attribute("className")?.Value ?? GetClassFromFullName(name);
            var methodName = testMethod?.Attribute("name")?.Value ?? GetMethodFromFullName(name);
            var codeBase = testMethod?.Attribute("codeBase")?.Value;

            var categories = unitTest
                .Elements(Ns + "TestCategory")
                .Elements(Ns + "TestCategoryItem")
                .Select(item => item.Attribute("TestCategory")?.Value)
                .Where(value => !string.IsNullOrEmpty(value))
                .Cast<string>()
                .ToList();

            var def = new TrxDefinition(className, methodName, codeBase, categories);
            if (id is not null) definitions[id] = def;
            definitionsByName[name] = def;
        }

        var tests = new List<DiscoveredTest>();
        var states = new Dictionary<string, TestNodeState>(StringComparer.Ordinal);
        var seenFullNames = new HashSet<string>(StringComparer.Ordinal);
        TimeSpan totalDuration = TimeSpan.Zero;

        foreach (var result in root.Elements(Ns + "Results").Elements(Ns + "UnitTestResult"))
        {
            var fullName = result.Attribute("testName")?.Value;
            if (string.IsNullOrEmpty(fullName)) continue;

            var outcomeRaw = result.Attribute("outcome")?.Value ?? "NotExecuted";
            var status = MapOutcome(outcomeRaw);
            var duration = ParseDuration(result.Attribute("duration")?.Value);
            if (duration is not null) totalDuration += duration.Value;

            var (errorMessage, stackTrace, stdOut, stdErr) = ExtractOutput(result);

            var testId = result.Attribute("testId")?.Value;
            var def = (testId is not null && definitions.TryGetValue(testId, out var byId))
                ? byId
                : (definitionsByName.TryGetValue(fullName, out var byName)
                    ? byName
                    : new TrxDefinition(GetClassFromFullName(fullName), GetMethodFromFullName(fullName), null, []));

            var assemblyName = !string.IsNullOrEmpty(def.CodeBase)
                ? (Path.GetFileNameWithoutExtension(def.CodeBase) ?? "Unknown")
                : "Unknown";

            if (seenFullNames.Add(fullName))
            {
                tests.Add(new DiscoveredTest(
                    TestClass: null,
                    TestMethod: null,
                    FullName: fullName,
                    AssemblyName: assemblyName,
                    IsIgnored: status == TestStatus.Skipped,
                    IgnoreReason: null,
                    CodeBase: def.CodeBase,
                    Categories: def.Categories.Count > 0 ? def.Categories : null,
                    ClassFullName: !string.IsNullOrEmpty(def.ClassName) ? def.ClassName : null));
            }

            states[fullName] = new TestNodeState(fullName, status, duration, errorMessage, stackTrace, stdOut, stdErr);
        }

        var counters = root.Element(Ns + "ResultSummary")?.Element(Ns + "Counters");
        var total = ReadIntAttribute(counters, "total", tests.Count);
        var passed = ReadIntAttribute(counters, "passed", states.Values.Count(s => s.Status == TestStatus.Passed));
        var failed = ReadIntAttribute(counters, "failed", states.Values.Count(s => s.Status == TestStatus.Failed));
        var skipped = ReadIntAttribute(counters, "notExecuted", states.Values.Count(s => s.Status == TestStatus.Skipped));

        var summary = new TrxSummary(total, passed, failed, skipped, totalDuration, runName, fileName);
        return new TrxParseResult(tests, states, summary);
    }

    private static TestStatus MapOutcome(string outcome) => outcome switch
    {
        "Passed" => TestStatus.Passed,
        "Failed" => TestStatus.Failed,
        _ => TestStatus.Skipped,
    };

    private static TimeSpan? ParseDuration(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        return TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var ts) ? ts : null;
    }

    private static (string? Message, string? StackTrace, string? StdOut, string? StdErr) ExtractOutput(XElement result)
    {
        var output = result.Element(Ns + "Output");
        if (output is null) return (null, null, null, null);

        var errorInfo = output.Element(Ns + "ErrorInfo");
        var message = errorInfo?.Element(Ns + "Message")?.Value;
        var stackTrace = errorInfo?.Element(Ns + "StackTrace")?.Value;
        var stdOut = output.Element(Ns + "StdOut")?.Value;
        var stdErr = output.Element(Ns + "StdErr")?.Value;

        return (string.IsNullOrEmpty(message) ? null : message,
                string.IsNullOrEmpty(stackTrace) ? null : stackTrace,
                string.IsNullOrEmpty(stdOut) ? null : stdOut,
                string.IsNullOrEmpty(stdErr) ? null : stdErr);
    }

    private static int ReadIntAttribute(XElement? element, string name, int fallback)
    {
        var raw = element?.Attribute(name)?.Value;
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
    }

    private static string GetClassFromFullName(string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        return dot > 0 ? fullName[..dot] : fullName;
    }

    private static string GetMethodFromFullName(string fullName)
    {
        var dot = fullName.LastIndexOf('.');
        return dot > 0 && dot + 1 < fullName.Length ? fullName[(dot + 1)..] : fullName;
    }

    private sealed record TrxDefinition(string ClassName, string MethodName, string? CodeBase, IReadOnlyList<string> Categories);
}
