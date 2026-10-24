using System.Text.Json;

namespace Motus;

/// <summary>
/// Converts between the values CDP script commands carry and the ones WebDriver BiDi expects:
/// remote object ids and remote references, CDP call arguments and BiDi local values, and BiDi
/// remote values and the plain JSON a CDP call made by value returns.
/// </summary>
/// <remarks>
/// <para>
/// The engine keeps one opaque string per remote object, the CDP <c>objectId</c>, but BiDi has two
/// ways to name one. A handle names any object in the realm that made it, and only while that
/// realm lives. A shared id names a DOM node, is the same every time the node is serialized, and
/// is accepted from any realm of the node's document. Nodes are handed out by shared id, so an
/// element found by one query is the same id when another query finds it again, and every other
/// object by handle. A shared id carries <see cref="SharedIdPrefix"/> so it can be told apart when
/// it comes back as <c>this</c> or as an argument; a handle is passed through as it is.
/// </para>
/// <para>
/// BiDi serializes structured values as typed remote values, an object as a list of key and value
/// pairs, rather than as JSON. A CDP call made by value expects the JSON <c>JSON.stringify</c>
/// would give, so the remote value is rebuilt into that shape, and a CDP argument is turned into
/// the local value BiDi expects the same way in reverse.
/// </para>
/// </remarks>
internal static class BiDiScriptValues
{
    /// <summary>
    /// Marks an object id that is a BiDi shared id rather than a handle.
    /// </summary>
    internal const string SharedIdPrefix = "sharedId:";

    /// <summary>
    /// The object id for a BiDi remote value: its shared id for a node, its handle otherwise, or
    /// null when it has neither.
    /// </summary>
    internal static string? ToObjectId(JsonElement remoteValue)
    {
        if (remoteValue.ValueKind != JsonValueKind.Object)
            return null;

        var sharedId = remoteValue.GetStringOrNull("sharedId");
        if (sharedId is not null)
            return SharedIdPrefix + sharedId;

        return remoteValue.GetStringOrNull("handle");
    }

    /// <summary>
    /// The shared id inside an object id, or null when the object id is a handle.
    /// </summary>
    internal static string? SharedIdOf(string objectId) =>
        objectId.StartsWith(SharedIdPrefix, StringComparison.Ordinal)
            ? objectId[SharedIdPrefix.Length..]
            : null;

    /// <summary>
    /// The BiDi remote reference for an object id: <c>{ "sharedId": ... }</c> for a node and
    /// <c>{ "handle": ... }</c> for anything else.
    /// </summary>
    internal static JsonElement ToRemoteReference(string objectId)
    {
        var sharedId = SharedIdOf(objectId);
        return sharedId is not null
            ? JsonBuilder.FromObject(new { sharedId })
            : JsonBuilder.FromObject(new { handle = objectId });
    }

    /// <summary>
    /// The BiDi local value for a CDP call argument, which names a remote object, carries a value,
    /// carries an unserializable number, or is empty for <c>undefined</c>.
    /// </summary>
    internal static JsonElement ToLocalValue(JsonElement cdpArgument)
    {
        var objectId = cdpArgument.GetStringOrNull("objectId");
        if (objectId is not null)
            return ToRemoteReference(objectId);

        var unserializable = cdpArgument.GetStringOrNull("unserializableValue");
        if (unserializable is not null)
        {
            return unserializable.EndsWith('n')
                ? JsonBuilder.FromObject(new { type = "bigint", value = unserializable[..^1] })
                : JsonBuilder.FromObject(new { type = "number", value = unserializable });
        }

        return cdpArgument.TryGetProperty("value", out var value)
            ? FromJson(value)
            : JsonBuilder.FromObject(new { type = "undefined" });
    }

    /// <summary>
    /// The BiDi local value for a JSON value.
    /// </summary>
    internal static JsonElement FromJson(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteLocalValue(writer, value);

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    private static void WriteLocalValue(Utf8JsonWriter writer, JsonElement value)
    {
        writer.WriteStartObject();
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                writer.WriteString("type", "string");
                writer.WritePropertyName("value");
                value.WriteTo(writer);
                break;
            case JsonValueKind.Number:
                writer.WriteString("type", "number");
                writer.WritePropertyName("value");
                value.WriteTo(writer);
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteString("type", "boolean");
                writer.WriteBoolean("value", value.GetBoolean());
                break;
            case JsonValueKind.Array:
                writer.WriteString("type", "array");
                writer.WriteStartArray("value");
                foreach (var item in value.EnumerateArray())
                    WriteLocalValue(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Object:
                writer.WriteString("type", "object");
                writer.WriteStartArray("value");
                foreach (var property in value.EnumerateObject())
                {
                    writer.WriteStartArray();
                    writer.WriteStringValue(property.Name);
                    WriteLocalValue(writer, property.Value);
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
                break;
            case JsonValueKind.Null:
                writer.WriteString("type", "null");
                break;
            default:
                writer.WriteString("type", "undefined");
                break;
        }
        writer.WriteEndObject();
    }

    /// <summary>
    /// The CDP remote object for a BiDi remote value: its type, its object id when it has one, and
    /// its value as JSON when it has one.
    /// </summary>
    internal static object ToCdpRemoteObject(JsonElement remoteValue)
    {
        var type = remoteValue.GetStringOrNull("type") ?? "undefined";
        var (cdpType, cdpSubtype) = MapType(type);
        var objectId = ToObjectId(remoteValue);

        // A number JSON cannot hold arrives as a string, and CDP reports it the same way.
        if (type == "number" && remoteValue.TryGetProperty("value", out var number)
            && number.ValueKind == JsonValueKind.String)
        {
            return new { type = cdpType, unserializableValue = number.GetString() };
        }

        if (type == "bigint" && remoteValue.GetStringOrNull("value") is { } digits)
            return new { type = cdpType, unserializableValue = digits + "n" };

        var json = ToJson(remoteValue);
        return json is null
            ? new { type = cdpType, subtype = cdpSubtype, objectId }
            : new { type = cdpType, subtype = cdpSubtype, value = (object?)json, objectId };
    }

    /// <summary>
    /// Rebuilds a BiDi remote value as the JSON <c>JSON.stringify</c> would give for it, or null
    /// when there is none, as for <c>undefined</c> and functions.
    /// </summary>
    /// <remarks>
    /// Arrays and plain objects are rebuilt from their serialized contents. A node, a map, a
    /// regular expression and any other object becomes an empty object, which is what CDP returns
    /// for them by value too. A value BiDi did not serialize, because it was already seen higher up
    /// or lies past the serialization depth, has no contents and becomes an empty array or object.
    /// </remarks>
    internal static JsonElement? ToJson(JsonElement remoteValue)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            if (!WriteJson(writer, remoteValue))
                return null;
        }

        using var doc = JsonDocument.Parse(stream.ToArray());
        return doc.RootElement.Clone();
    }

    // Writes the JSON for a remote value, or nothing and false when JSON has no value for it, so a
    // caller writing an object leaves the key out and one writing an array writes null instead.
    private static bool WriteJson(Utf8JsonWriter writer, JsonElement remoteValue)
    {
        var type = remoteValue.GetStringOrNull("type");
        remoteValue.TryGetProperty("value", out var value);

        switch (type)
        {
            case "undefined":
            case "function":
            case "symbol":
                return false;
            case "null":
                writer.WriteNullValue();
                return true;
            case "string":
            case "bigint":
            case "date":
                writer.WriteStringValue(value.ValueKind == JsonValueKind.String ? value.GetString() : string.Empty);
                return true;
            case "boolean":
                writer.WriteBooleanValue(value.ValueKind == JsonValueKind.True);
                return true;
            case "number":
                WriteNumber(writer, value);
                return true;
            case "array":
                writer.WriteStartArray();
                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in value.EnumerateArray())
                    {
                        if (!WriteJson(writer, item))
                            writer.WriteNullValue();
                    }
                }
                writer.WriteEndArray();
                return true;
            case "object":
                writer.WriteStartObject();
                if (value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pair in value.EnumerateArray())
                    {
                        if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2)
                            continue;

                        var key = pair[0].ValueKind == JsonValueKind.String
                            ? pair[0].GetString()!
                            : pair[0].GetStringOrNull("value") ?? string.Empty;

                        // Written to a scratch buffer first, because a value JSON leaves out takes
                        // its key with it.
                        using var buffer = new MemoryStream();
                        using (var inner = new Utf8JsonWriter(buffer))
                        {
                            if (!WriteJson(inner, pair[1]))
                                continue;
                        }

                        writer.WritePropertyName(key);
                        using var doc = JsonDocument.Parse(buffer.ToArray());
                        doc.RootElement.WriteTo(writer);
                    }
                }
                writer.WriteEndObject();
                return true;
            default:
                writer.WriteStartObject();
                writer.WriteEndObject();
                return true;
        }
    }

    // NaN and the infinities have no JSON spelling and become null, as JSON.stringify makes them;
    // negative zero is zero.
    private static void WriteNumber(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            value.WriteTo(writer);
            return;
        }

        if (value.ValueKind == JsonValueKind.String && value.GetString() == "-0")
        {
            writer.WriteNumberValue(0);
            return;
        }

        writer.WriteNullValue();
    }

    private static (string type, string? subtype) MapType(string biDiType) => biDiType switch
    {
        "string" => ("string", null),
        "number" => ("number", null),
        "bigint" => ("bigint", null),
        "boolean" => ("boolean", null),
        "symbol" => ("symbol", null),
        "function" => ("function", null),
        "null" => ("object", "null"),
        "undefined" => ("undefined", null),
        "array" => ("object", "array"),
        "node" => ("object", "node"),
        "regexp" => ("object", "regexp"),
        "date" => ("object", "date"),
        "map" => ("object", "map"),
        "set" => ("object", "set"),
        "error" => ("object", "error"),
        "promise" => ("object", "promise"),
        "object" => ("object", null),
        _ => ("object", null)
    };
}
