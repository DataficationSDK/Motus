using System;

namespace Motus.Codegen.Model;

/// <summary>
/// Describes a discovered plugin type for the source generator pipeline.
/// Implements structural equality for incremental generator caching.
/// </summary>
internal sealed class PluginTypeInfo : IEquatable<PluginTypeInfo>
{
    public PluginTypeInfo(string fullyQualifiedName, string assemblyName)
    {
        FullyQualifiedName = fullyQualifiedName;
        AssemblyName = assemblyName;
    }

    public string FullyQualifiedName { get; }
    public string AssemblyName { get; }

    public bool Equals(PluginTypeInfo other)
    {
        if (other is null) return false;
        return FullyQualifiedName == other.FullyQualifiedName
            && AssemblyName == other.AssemblyName;
    }

    public override bool Equals(object obj) => Equals(obj as PluginTypeInfo);

    public override int GetHashCode()
    {
        unchecked
        {
            return (FullyQualifiedName.GetHashCode() * 397) ^ AssemblyName.GetHashCode();
        }
    }
}
