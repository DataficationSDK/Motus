using System.Reflection;

namespace Motus.Runner.Services.Models;

/// <summary>
/// A test the visual runner lists. A test found by discovery runs in a test host process of its
/// assembly (at <paramref name="CodeBase"/>), so <paramref name="TestClass"/>,
/// <paramref name="TestMethod"/> and <paramref name="Arguments"/> are null and it is known by
/// <paramref name="ClassFullName"/> and <paramref name="MethodName"/>. One built from reflection
/// metadata runs in this process. <paramref name="TestId"/> identifies a discovered test apart from
/// its name, which a data row's data source may choose differently each time it is read.
/// </summary>
public sealed record DiscoveredTest(
    Type? TestClass,
    MethodInfo? TestMethod,
    string FullName,
    string AssemblyName,
    bool IsIgnored,
    string? IgnoreReason = null,
    string? CodeBase = null,
    IReadOnlyList<string>? Categories = null,
    string? ClassFullName = null,
    object?[]? Arguments = null,
    string? MethodName = null,
    string? TestId = null);
