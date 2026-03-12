namespace Motus.Abstractions;

/// <summary>
/// Marks a class as a Motus plugin entry point. The Motus source generator scans for this attribute
/// to produce plugin registration code at compile time.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class MotusPluginAttribute : Attribute { }
