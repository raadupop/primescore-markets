using System.Globalization;
using System.Text.Json;
using PrimeScore.SharedKernel;

namespace PrimeScore.Engine.Host.Components.Shared;

/// <summary>One display format for times, numbers and payloads across screens.</summary>
internal static class Fmt
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Utc(DateTimeOffset? instant) =>
        instant is { } value ? value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : "—";

    public static string Date(DateTimeOffset? instant) =>
        instant is { } value ? value.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "—";

    public static string Number(double? value, int decimals = 2) =>
        value is { } number ? number.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) : "—";

    public static string Signed(double? value, int decimals = 4) =>
        value is { } number ? number.ToString("+0." + new string('0', decimals) + ";-0." + new string('0', decimals) + ";0", CultureInfo.InvariantCulture) : "—";

    public static string Category(SourceCategory category) => category.ToWireName();

    public static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Json(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement, Indented);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    /// <summary>A length of time: minutes below an hour, hours below two days, then days.</summary>
    public static string Duration(double? seconds) => seconds switch
    {
        null => "—",
        < 3600 => string.Create(CultureInfo.InvariantCulture, $"{seconds.Value / 60:0} min"),
        < 172800 => string.Create(CultureInfo.InvariantCulture, $"{seconds.Value / 3600:0} h"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{seconds.Value / 86400:0} d"),
    };

    public static string Age(DateTimeOffset? instant, DateTimeOffset now)
    {
        if (instant is not { } value)
        {
            return "never";
        }

        var age = now - value;
        return age.TotalDays >= 2 ? $"{(int)age.TotalDays} days ago"
            : age.TotalHours >= 2 ? $"{(int)age.TotalHours} hours ago"
            : $"{Math.Max(0, (int)age.TotalMinutes)} minutes ago";
    }
}
