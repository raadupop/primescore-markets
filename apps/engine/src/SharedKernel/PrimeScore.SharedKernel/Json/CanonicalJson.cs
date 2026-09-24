using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace PrimeScore.SharedKernel.Json;

/// <summary>
/// Deterministic JSON for hashing and storage: snake_case property names, object keys in
/// ordinal order, no insignificant whitespace, shortest round-trip numbers. Non-finite
/// numbers are rejected, so a ledger payload can never carry NaN or infinity.
/// </summary>
public static class CanonicalJson
{
    public static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
    };

    public static string Serialize<T>(T value)
    {
        var node = JsonSerializer.SerializeToNode(value, SerializerOptions);
        return Write(node);
    }

    /// <summary>Re-serializes arbitrary JSON text in canonical form.</summary>
    public static string Canonicalize(string json) => Write(JsonNode.Parse(json));

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, SerializerOptions)
        ?? throw new JsonException($"Canonical JSON did not contain a {typeof(T).Name}.");

    private static string Write(JsonNode? node)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            WriteNode(writer, node);
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteNode(Utf8JsonWriter writer, JsonNode? node)
    {
        switch (node)
        {
            case null:
                writer.WriteNullValue();
                break;
            case JsonObject obj:
                writer.WriteStartObject();
                foreach (var (key, value) in obj.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(key);
                    WriteNode(writer, value);
                }

                writer.WriteEndObject();
                break;
            case JsonArray array:
                writer.WriteStartArray();
                foreach (var item in array)
                {
                    WriteNode(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValue value when value.GetValueKind() == JsonValueKind.Number:
                WriteNumber(writer, value);
                break;
            case JsonValue value:
                value.WriteTo(writer);
                break;
        }
    }

    /// <summary>
    /// One spelling per number: integers that fit 64 bits as written, everything else as the
    /// shortest round-trip double, so <c>1.0</c>, <c>1</c> and <c>1E0</c> canonicalize alike.
    /// </summary>
    private static void WriteNumber(Utf8JsonWriter writer, JsonValue value)
    {
        var element = JsonSerializer.SerializeToElement(value);
        if (element.TryGetInt64(out var integer))
        {
            writer.WriteNumberValue(integer);
            return;
        }

        if (!element.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            throw new JsonException("Canonical JSON rejects non-finite or out-of-range numbers.");
        }

        if (number == Math.Floor(number) && Math.Abs(number) < 9.2e18)
        {
            writer.WriteNumberValue((long)number);
            return;
        }

        writer.WriteNumberValue(number);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DictionaryKeyPolicy = null,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.Strict,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
