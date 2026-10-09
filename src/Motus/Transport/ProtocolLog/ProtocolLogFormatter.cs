using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Motus;

/// <summary>
/// Writes a protocol payload as compact JSON for one log line, trimming long strings and replacing
/// secrets as it goes.
/// </summary>
/// <remarks>
/// <para>
/// Redaction goes by property name, not by method, and applies to both directions and both
/// protocols. That matters because the browser pushes cookie and authorization values in events
/// nobody asked for, such as <c>Network.requestWillBeSentExtraInfo</c>, so a list of the methods
/// Motus sends would miss most of them.
/// </para>
/// <para>
/// The walk uses <see cref="Utf8JsonWriter"/> over <see cref="JsonElement"/> only, so it needs no
/// reflection and stays safe for trimmed and native AOT builds.
/// </para>
/// </remarks>
internal static class ProtocolLogFormatter
{
    /// <summary>
    /// The longest a payload may be once formatted, in bytes. Past this it is cut, and the line is
    /// no longer valid JSON. Large trees such as a DOM snapshot or a coverage report reach it.
    /// </summary>
    internal const int MaxPayloadBytes = 64 * 1024;

    /// <summary>What a redacted value is replaced with.</summary>
    internal const string Redacted = "<redacted>";

    private static readonly JsonWriterOptions s_writerOptions = new()
    {
        // Leaves non-ASCII text readable. The output is a log line, never markup.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = true,
    };

    /// <summary>Properties holding a set of HTTP headers, keyed by name or as name and value pairs.</summary>
    private static readonly HashSet<string> s_headerContainers = new(StringComparer.Ordinal)
    {
        "headers", "requestHeaders", "responseHeaders", "extraHTTPHeaders",
    };

    /// <summary>Properties holding one cookie or a list of them.</summary>
    private static readonly HashSet<string> s_cookieContainers = new(StringComparer.Ordinal)
    {
        "cookies", "cookie", "associatedCookies", "blockedCookies", "exemptedCookies",
    };

    /// <summary>Properties replaced whole, because they carry raw header lines.</summary>
    private static readonly HashSet<string> s_rawHeaderText = new(StringComparer.Ordinal)
    {
        "headersText", "requestHeadersText", "binaryResponseHeaders", "cookieLine",
    };

    /// <summary>Properties carrying a request body, which is where a login form's password goes.</summary>
    private static readonly HashSet<string> s_requestBodies = new(StringComparer.Ordinal)
    {
        "postData", "postDataEntries",
    };

    /// <summary>Methods that set one cookie with its name and value at the root of the params.</summary>
    private static readonly HashSet<string> s_rootCookieMethods = new(StringComparer.Ordinal)
    {
        "Network.setCookie",
    };

    private enum Scope
    {
        Plain,
        Cookie,
    }

    /// <summary>
    /// Formats <paramref name="payload"/> for a log line.
    /// </summary>
    /// <param name="payload">The params, result or whole message. An undefined element formats as nothing.</param>
    /// <param name="method">The method the payload belongs to, when known. A few redactions depend on it.</param>
    /// <param name="maxValueLength">The longest a string may be before it is trimmed; zero or less for no limit.</param>
    /// <param name="redactSecrets">Whether to replace secrets.</param>
    internal static string Format(JsonElement payload, string? method, int maxValueLength, bool redactSecrets)
    {
        if (payload.ValueKind == JsonValueKind.Undefined)
            return string.Empty;

        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer, s_writerOptions))
        {
            var context = new Context(maxValueLength, redactSecrets);
            var rootCookie = redactSecrets && method is not null && s_rootCookieMethods.Contains(method);

            if (rootCookie && payload.ValueKind == JsonValueKind.Object)
                WriteObject(writer, payload, Scope.Cookie, context);
            else
                WriteValue(writer, payload, Scope.Plain, context);
        }

        return Cap(buffer.WrittenSpan);
    }

    /// <summary>
    /// Formats a message that could not be parsed, as text on one line.
    /// </summary>
    internal static string FormatMalformed(ReadOnlySpan<byte> message, int maxValueLength)
    {
        var text = Encoding.UTF8.GetString(message);
        text = Trim(text, maxValueLength);
        return text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    }

    /// <summary>
    /// Formats a string as a quoted JSON string, trimmed like any other value.
    /// </summary>
    internal static string Quote(string value, int maxValueLength)
    {
        var buffer = new ArrayBufferWriter<byte>(value.Length + 2);
        using (var writer = new Utf8JsonWriter(buffer, s_writerOptions))
            writer.WriteStringValue(Trim(value, maxValueLength));

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Cuts a string to <paramref name="maxValueLength"/> characters and says how many went.
    /// </summary>
    internal static string Trim(string value, int maxValueLength)
    {
        if (maxValueLength <= 0 || value.Length <= maxValueLength)
            return value;

        var keep = maxValueLength;

        // Never split a surrogate pair, which would leave half a character behind.
        if (char.IsHighSurrogate(value[keep - 1]))
            keep--;

        return string.Create(CultureInfo.InvariantCulture,
            $"{value.AsSpan(0, keep)}…[+{value.Length - keep} chars]");
    }

    private static string Cap(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length <= MaxPayloadBytes)
            return Encoding.UTF8.GetString(utf8);

        var cut = MaxPayloadBytes;

        // Back off to the start of a character so the kept part decodes cleanly.
        while (cut > 0 && (utf8[cut] & 0xC0) == 0x80)
            cut--;

        return string.Create(CultureInfo.InvariantCulture,
            $"{Encoding.UTF8.GetString(utf8[..cut])}…[+{utf8.Length - cut} bytes]");
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement element, Scope scope, Context context)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(writer, element, scope, context);
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteValue(writer, item, scope, context);
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(Trim(element.GetString()!, context.MaxValueLength));
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static void WriteObject(Utf8JsonWriter writer, JsonElement element, Scope scope, Context context)
    {
        writer.WriteStartObject();

        foreach (var property in element.EnumerateObject())
        {
            writer.WritePropertyName(property.Name);

            if (!context.RedactSecrets)
            {
                WriteValue(writer, property.Value, Scope.Plain, context);
                continue;
            }

            var name = property.Name;
            var value = property.Value;

            if (string.Equals(name, "password", StringComparison.OrdinalIgnoreCase) ||
                s_rawHeaderText.Contains(name) ||
                (scope == Scope.Cookie && name == "value"))
            {
                writer.WriteStringValue(Redacted);
            }
            else if (s_requestBodies.Contains(name))
            {
                writer.WriteStringValue(DescribeRedactedBody(value));
            }
            else if (s_headerContainers.Contains(name))
            {
                WriteHeaders(writer, value, context);
            }
            else if (s_cookieContainers.Contains(name))
            {
                WriteValue(writer, value, Scope.Cookie, context);
            }
            else
            {
                WriteValue(writer, value, scope, context);
            }
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes a set of headers with the values of sensitive ones replaced. CDP mostly keys headers
    /// by name; Fetch and BiDi list them as name and value pairs, and BiDi wraps each value in an
    /// object of its own, which is replaced whole.
    /// </summary>
    private static void WriteHeaders(Utf8JsonWriter writer, JsonElement headers, Context context)
    {
        if (headers.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var header in headers.EnumerateObject())
            {
                writer.WritePropertyName(header.Name);
                if (IsSensitiveHeader(header.Name))
                    writer.WriteStringValue(Redacted);
                else
                    WriteValue(writer, header.Value, Scope.Plain, context);
            }
            writer.WriteEndObject();
            return;
        }

        if (headers.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var entry in headers.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object &&
                    entry.TryGetProperty("name", out var nameElement) &&
                    nameElement.ValueKind == JsonValueKind.String &&
                    IsSensitiveHeader(nameElement.GetString()!))
                {
                    writer.WriteStartObject();
                    foreach (var field in entry.EnumerateObject())
                    {
                        writer.WritePropertyName(field.Name);
                        if (field.Name == "value")
                            writer.WriteStringValue(Redacted);
                        else
                            WriteValue(writer, field.Value, Scope.Plain, context);
                    }
                    writer.WriteEndObject();
                }
                else
                {
                    WriteValue(writer, entry, Scope.Plain, context);
                }
            }
            writer.WriteEndArray();
            return;
        }

        WriteValue(writer, headers, Scope.Plain, context);
    }

    /// <summary>
    /// Whether a header's value is a credential. Matches the well-known names and anything that
    /// looks like it carries a token or a key.
    /// </summary>
    internal static bool IsSensitiveHeader(string name)
    {
        return name.Equals("cookie", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("api-key", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("apikey", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeRedactedBody(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => string.Create(CultureInfo.InvariantCulture,
            $"<redacted {value.GetString()!.Length} chars>"),
        JsonValueKind.Array => string.Create(CultureInfo.InvariantCulture,
            $"<redacted {value.GetArrayLength()} entries>"),
        _ => Redacted,
    };

    private readonly record struct Context(int MaxValueLength, bool RedactSecrets);
}
