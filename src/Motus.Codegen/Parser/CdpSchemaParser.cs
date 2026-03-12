using System.Collections.Immutable;
using System.Text.Json;
using Motus.Codegen.Model;

namespace Motus.Codegen.Parser;

/// <summary>
/// Parses CDP protocol JSON into an immutable domain model.
/// </summary>
internal static class CdpSchemaParser
{
    public static ImmutableArray<CdpDomain> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("domains", out var domainsElement))
            return ImmutableArray<CdpDomain>.Empty;

        var builder = ImmutableArray.CreateBuilder<CdpDomain>();

        foreach (var domainEl in domainsElement.EnumerateArray())
        {
            builder.Add(ParseDomain(domainEl));
        }

        return builder.ToImmutable();
    }

    private static CdpDomain ParseDomain(JsonElement el)
    {
        var name = el.GetProperty("domain").GetString()!;
        var deprecated = GetBool(el, "deprecated");
        var experimental = GetBool(el, "experimental");

        var types = ParseArray(el, "types", ParseType);
        var commands = ParseArray(el, "commands", ParseCommand);
        var events = ParseArray(el, "events", ParseEvent);

        return new CdpDomain(name, types, commands, events, deprecated, experimental);
    }

    private static CdpType ParseType(JsonElement el)
    {
        var id = el.GetProperty("id").GetString()!;
        var typeName = el.TryGetProperty("type", out var tp) ? tp.GetString() : null;
        var deprecated = GetBool(el, "deprecated");
        var experimental = GetBool(el, "experimental");

        var enumValues = ImmutableArray<string>.Empty;
        if (el.TryGetProperty("enum", out var enumEl))
        {
            var eb = ImmutableArray.CreateBuilder<string>();
            foreach (var v in enumEl.EnumerateArray())
                eb.Add(v.GetString()!);
            enumValues = eb.ToImmutable();
        }

        var properties = ParseArray(el, "properties", ParseProperty);

        string? arrayItemRef = null;
        string? arrayItemType = null;
        if (el.TryGetProperty("items", out var itemsEl))
        {
            arrayItemRef = GetString(itemsEl, "$ref");
            arrayItemType = GetString(itemsEl, "type");
        }

        CdpTypeKind kind;
        if (typeName == "string" && enumValues.Length > 0)
            kind = CdpTypeKind.StringEnum;
        else if (typeName == "object")
            kind = CdpTypeKind.Object;
        else if (typeName == "array")
            kind = CdpTypeKind.ArrayType;
        else
            kind = CdpTypeKind.Alias;

        return new CdpType(id, typeName, kind, enumValues, properties, arrayItemRef, arrayItemType, deprecated, experimental);
    }

    private static CdpCommand ParseCommand(JsonElement el)
    {
        var name = el.GetProperty("name").GetString()!;
        var deprecated = GetBool(el, "deprecated");
        var experimental = GetBool(el, "experimental");
        var parameters = ParseArray(el, "parameters", ParseProperty);
        var returns = ParseArray(el, "returns", ParseProperty);

        return new CdpCommand(name, parameters, returns, deprecated, experimental);
    }

    private static CdpEvent ParseEvent(JsonElement el)
    {
        var name = el.GetProperty("name").GetString()!;
        var deprecated = GetBool(el, "deprecated");
        var experimental = GetBool(el, "experimental");
        var parameters = ParseArray(el, "parameters", ParseProperty);

        return new CdpEvent(name, parameters, deprecated, experimental);
    }

    private static CdpProperty ParseProperty(JsonElement el)
    {
        var name = el.GetProperty("name").GetString()!;
        var typeRef = GetString(el, "$ref");
        var typeName = GetString(el, "type");
        var optional = GetBool(el, "optional");
        var deprecated = GetBool(el, "deprecated");

        string? arrayItemRef = null;
        string? arrayItemType = null;
        if (el.TryGetProperty("items", out var itemsEl))
        {
            arrayItemRef = GetString(itemsEl, "$ref");
            arrayItemType = GetString(itemsEl, "type");
        }

        var inlineEnum = ImmutableArray<string>.Empty;
        if (el.TryGetProperty("enum", out var enumEl))
        {
            var eb = ImmutableArray.CreateBuilder<string>();
            foreach (var v in enumEl.EnumerateArray())
                eb.Add(v.GetString()!);
            inlineEnum = eb.ToImmutable();
        }

        return new CdpProperty(name, typeRef, typeName, optional, arrayItemRef, arrayItemType, deprecated, inlineEnum);
    }

    private static ImmutableArray<T> ParseArray<T>(JsonElement parent, string propertyName, Func<JsonElement, T> parser)
    {
        if (!parent.TryGetProperty(propertyName, out var arrayEl))
            return ImmutableArray<T>.Empty;

        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var item in arrayEl.EnumerateArray())
            builder.Add(parser(item));

        return builder.ToImmutable();
    }

    private static bool GetBool(JsonElement el, string propertyName)
    {
        return el.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.True;
    }

    private static string? GetString(JsonElement el, string propertyName)
    {
        return el.TryGetProperty(propertyName, out var prop) ? prop.GetString() : null;
    }
}
