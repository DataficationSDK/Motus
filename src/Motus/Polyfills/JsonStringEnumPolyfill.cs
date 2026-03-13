#if !NET9_0_OR_GREATER
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

// Polyfills for JsonStringEnumMemberNameAttribute and JsonStringEnumConverter<T>,
// which were introduced in .NET 9. These enable the source-generated CDP protocol
// enum types to compile and serialize correctly on .NET 8.

namespace System.Text.Json.Serialization;

[AttributeUsage(AttributeTargets.Field)]
internal sealed class JsonStringEnumMemberNameAttribute : Attribute
{
    public JsonStringEnumMemberNameAttribute(string name) => Name = name;
    public string Name { get; }
}

internal sealed class JsonStringEnumConverter<TEnum> : JsonConverterFactory where TEnum : struct, Enum
{
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(EnumMemberConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }

    private sealed class EnumMemberConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        private readonly Dictionary<T, string> _toStr = new();
        private readonly Dictionary<string, T> _fromStr = new(StringComparer.OrdinalIgnoreCase);

        public EnumMemberConverter()
        {
            foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                var value = (T)field.GetValue(null)!;
                var attr = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>();
                var name = attr?.Name ?? field.Name;
                _toStr[value] = name;
                _fromStr[name] = value;
            }
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var str = reader.GetString();
            if (str is not null && _fromStr.TryGetValue(str, out var val))
                return val;
            throw new JsonException($"Unable to convert \"{str}\" to {typeof(T).Name}.");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (_toStr.TryGetValue(value, out var name))
                writer.WriteStringValue(name);
            else
                writer.WriteStringValue(value.ToString());
        }
    }
}
#endif
