using Microsoft.CodeAnalysis;

namespace Motus.Analyzers;

internal static class SymbolHelper
{
    public static bool ImplementsAny(ITypeSymbol? type, params string[] interfaceFqns)
    {
        if (type is null) return false;

        foreach (var iface in type.AllInterfaces)
        {
            var fqn = GetFullyQualifiedName(iface);
            foreach (var target in interfaceFqns)
            {
                if (fqn == target) return true;
            }
        }

        return false;
    }

    public static bool IsTaskType(ITypeSymbol? type)
    {
        if (type is null) return false;
        var fqn = GetFullyQualifiedName(type);
        return fqn == KnownTypeNames.Task || type.OriginalDefinition is INamedTypeSymbol named
            && GetFullyQualifiedName(named) == KnownTypeNames.TaskOfT;
    }

    public static string GetFullyQualifiedName(ITypeSymbol symbol)
    {
        var parts = new System.Collections.Generic.List<string>();

        if (symbol is INamedTypeSymbol named && named.IsGenericType)
            parts.Add(named.MetadataName);
        else
            parts.Add(symbol.Name);

        var ns = symbol.ContainingNamespace;
        while (ns is not null && !ns.IsGlobalNamespace)
        {
            parts.Add(ns.Name);
            ns = ns.ContainingNamespace;
        }

        parts.Reverse();
        return string.Join(".", parts);
    }

    public static bool IsContainingTypeOneOf(IMethodSymbol method, params string[] typeFqns)
    {
        if (method.ContainingType is null) return false;
        var fqn = GetFullyQualifiedName(method.ContainingType);
        foreach (var target in typeFqns)
        {
            if (fqn == target) return true;
        }
        return false;
    }
}
