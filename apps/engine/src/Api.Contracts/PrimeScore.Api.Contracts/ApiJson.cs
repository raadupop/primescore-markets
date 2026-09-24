using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrimeScore.Api.Contracts;

/// <summary>
/// One wire format for server and client: enums as their contract strings (including inside
/// arrays, which the generator leaves without a converter) and absent optional fields omitted.
/// </summary>
public static class ApiJson
{
    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Converters.Add(new JsonStringEnumConverter());
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    }
}

public partial class PrimeScoreApiClient
{
    static partial void UpdateJsonSerializerSettings(JsonSerializerOptions settings) => ApiJson.Configure(settings);
}
