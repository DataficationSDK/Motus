using System.Reflection;

namespace Motus.Runner.Services.Models;

public sealed record DiscoveredTest(Type TestClass, MethodInfo TestMethod, string FullName, string AssemblyName, bool IsIgnored, string? IgnoreReason = null);
