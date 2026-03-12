using System.Collections.Immutable;
using Motus.Codegen.Model;

namespace Motus.Codegen.Emit;

/// <summary>
/// Resolves CDP type references to fully qualified C# type strings.
/// </summary>
internal sealed class TypeResolver
{
    private readonly Dictionary<string, ResolvedType> _registry = new(StringComparer.Ordinal);

    public TypeResolver(ImmutableArray<CdpDomain> allDomains)
    {
        // Pass 1: register all non-array types (no cross-references needed)
        foreach (var domain in allDomains)
        {
            foreach (var type in domain.Types)
            {
                if (type.Kind == CdpTypeKind.ArrayType) continue;
                var qualifiedName = $"{domain.Name}.{type.Id}";
                _registry[qualifiedName] = ResolveDomainType(domain.Name, type);
            }
        }

        // Pass 2: register array types (may reference types from pass 1)
        foreach (var domain in allDomains)
        {
            foreach (var type in domain.Types)
            {
                if (type.Kind != CdpTypeKind.ArrayType) continue;
                var qualifiedName = $"{domain.Name}.{type.Id}";
                _registry[qualifiedName] = ResolveArrayAliasType(domain.Name, type);
            }
        }
    }

    /// <summary>
    /// Resolves a CDP property to a C# type string.
    /// </summary>
    public string Resolve(CdpProperty property, string currentDomain)
    {
        var baseType = ResolveBaseType(property.TypeRef, property.TypeName,
            property.ArrayItemRef, property.ArrayItemType, currentDomain);

        if (property.Optional)
            return MakeNullable(baseType);

        return baseType;
    }

    /// <summary>
    /// Resolves a CDP type reference string to a C# type string.
    /// </summary>
    public string ResolveRef(string typeRef, string currentDomain)
    {
        var qualified = typeRef.Contains(".")
            ? typeRef
            : $"{currentDomain}.{typeRef}";

        if (_registry.TryGetValue(qualified, out var resolved))
            return resolved.CSharpType;

        // Unknown ref; fall back to JsonElement
        return "System.Text.Json.JsonElement";
    }

    private string ResolveBaseType(string? typeRef, string? typeName,
        string? arrayItemRef, string? arrayItemType, string currentDomain)
    {
        // $ref takes priority
        if (typeRef != null)
            return ResolveRef(typeRef, currentDomain);

        return typeName switch
        {
            "string" => "string",
            "integer" => "long",
            "number" => "double",
            "boolean" => "bool",
            "array" => ResolveArrayType(arrayItemRef, arrayItemType, currentDomain),
            "object" => "System.Text.Json.JsonElement",
            "any" => "System.Text.Json.JsonElement",
            _ => "System.Text.Json.JsonElement"
        };
    }

    private string ResolveArrayType(string? itemRef, string? itemType, string currentDomain)
    {
        if (itemRef != null)
            return ResolveRef(itemRef, currentDomain) + "[]";

        var elementType = itemType switch
        {
            "string" => "string",
            "integer" => "long",
            "number" => "double",
            "boolean" => "bool",
            "any" => "System.Text.Json.JsonElement",
            "object" => "System.Text.Json.JsonElement",
            _ => "System.Text.Json.JsonElement"
        };

        return elementType + "[]";
    }

    private static string MakeNullable(string type)
    {
        // Arrays and JsonElement are reference types, just add ?
        return type + "?";
    }

    private ResolvedType ResolveDomainType(string domainName, CdpType type)
    {
        var domainClass = $"{domainName}Domain";

        return type.Kind switch
        {
            CdpTypeKind.StringEnum => new ResolvedType($"Motus.Protocol.{domainClass}.{NamingHelper.ToPascalCase(type.Id)}"),
            CdpTypeKind.Object => new ResolvedType($"Motus.Protocol.{domainClass}.{NamingHelper.ToPascalCase(type.Id)}"),
            CdpTypeKind.Alias => ResolveAliasType(type),
            CdpTypeKind.ArrayType => ResolveArrayAliasType(domainName, type),
            _ => new ResolvedType("System.Text.Json.JsonElement")
        };
    }

    private static ResolvedType ResolveAliasType(CdpType type)
    {
        return new ResolvedType(type.UnderlyingType switch
        {
            "string" => "string",
            "integer" => "long",
            "number" => "double",
            "boolean" => "bool",
            _ => "string"
        });
    }

    private ResolvedType ResolveArrayAliasType(string domainName, CdpType type)
    {
        if (type.ArrayItemRef != null)
        {
            var elementType = ResolveRef(type.ArrayItemRef, domainName);
            return new ResolvedType(elementType + "[]");
        }

        var primitiveType = type.ArrayItemType switch
        {
            "string" => "string",
            "integer" => "long",
            "number" => "double",
            "boolean" => "bool",
            _ => "System.Text.Json.JsonElement"
        };

        return new ResolvedType(primitiveType + "[]");
    }

    private readonly record struct ResolvedType(string CSharpType);
}
