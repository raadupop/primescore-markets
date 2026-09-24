using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrimeScore.Api.Contracts;

/// <summary>
/// Contract schema <c>SignalInput</c>. Hand-written because the code generator resolves the
/// schema's non-discriminated <c>oneOf</c> <c>structured_payload</c> to its first branch.
/// Reading is tolerant and keeps the element as received, so the server can reject each
/// malformed signal individually (SRS SIG-002) instead of failing the whole batch.
/// </summary>
[JsonConverter(typeof(SignalInputJsonConverter))]
public sealed class SignalInput
{
    public SourceCategory Source_category { get; set; }

    public string Source_identifier { get; set; } = "";

    public DateTimeOffset Timestamp { get; set; }

    public PayloadType Payload_type { get; set; }

    /// <summary>
    /// One of <see cref="MarketDataPayload"/>, <see cref="MacroeconomicPayload"/>,
    /// <see cref="GeopoliticalPayload"/>, <see cref="CrossAssetFlowPayload"/>, or a raw
    /// <see cref="JsonElement"/>. Serialized by its runtime type.
    /// </summary>
    public object? Structured_payload { get; set; }

    public UnstructuredPayload? Unstructured_payload { get; set; }

    /// <summary>The element as received on the wire; null for instances built in code.</summary>
    [JsonIgnore]
    public JsonElement? Raw { get; internal set; }
}

/// <summary>Wire format for <see cref="SignalInput"/>; field names follow the contract.</summary>
public sealed class SignalInputJsonConverter : JsonConverter<SignalInput>
{
    public override SignalInput Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = JsonElement.ParseValue(ref reader);
        var signal = new SignalInput { Raw = element.Clone() };
        if (element.ValueKind != JsonValueKind.Object)
        {
            return signal;
        }

        if (TryString(element, "source_category", out var category) && ApiEnum.TryParse<SourceCategory>(category, out var parsedCategory))
        {
            signal.Source_category = parsedCategory;
        }

        if (TryString(element, "source_identifier", out var identifier))
        {
            signal.Source_identifier = identifier;
        }

        if (element.TryGetProperty("timestamp", out var timestamp) && timestamp.ValueKind == JsonValueKind.String
            && timestamp.TryGetDateTimeOffset(out var parsedTimestamp))
        {
            signal.Timestamp = parsedTimestamp;
        }

        if (TryString(element, "payload_type", out var payloadType) && ApiEnum.TryParse<PayloadType>(payloadType, out var parsedPayloadType))
        {
            signal.Payload_type = parsedPayloadType;
        }

        if (element.TryGetProperty("structured_payload", out var structured) && structured.ValueKind != JsonValueKind.Null)
        {
            signal.Structured_payload = structured.Clone();
        }

        if (element.TryGetProperty("unstructured_payload", out var unstructured) && unstructured.ValueKind == JsonValueKind.Object)
        {
            try
            {
                signal.Unstructured_payload = unstructured.Deserialize<UnstructuredPayload>(options);
            }
            catch (JsonException)
            {
                // Malformed; the server validates the raw element and reports the field.
            }
        }

        return signal;
    }

    public override void Write(Utf8JsonWriter writer, SignalInput value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("source_category", ApiEnum.Value(value.Source_category));
        writer.WriteString("source_identifier", value.Source_identifier);
        writer.WriteString("timestamp", value.Timestamp);
        writer.WriteString("payload_type", ApiEnum.Value(value.Payload_type));
        if (value.Structured_payload is not null)
        {
            writer.WritePropertyName("structured_payload");
            JsonSerializer.Serialize(writer, value.Structured_payload, value.Structured_payload.GetType(), options);
        }

        if (value.Unstructured_payload is not null)
        {
            writer.WritePropertyName("unstructured_payload");
            JsonSerializer.Serialize(writer, value.Unstructured_payload, options);
        }

        writer.WriteEndObject();
    }

    private static bool TryString(JsonElement element, string name, out string value)
    {
        value = "";
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return true;
    }
}

/// <summary>Contract string values of generated enums (their <c>JsonStringEnumMemberName</c>).</summary>
public static class ApiEnum
{
    public static string Value<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        var name = value.ToString();
        var member = typeof(TEnum).GetField(name, BindingFlags.Public | BindingFlags.Static);
        return member?.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? name;
    }

    public static bool TryParse<TEnum>(string text, out TEnum value) where TEnum : struct, Enum
    {
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (string.Equals(Value(candidate), text, StringComparison.Ordinal))
            {
                value = candidate;
                return true;
            }
        }

        value = default;
        return false;
    }
}
