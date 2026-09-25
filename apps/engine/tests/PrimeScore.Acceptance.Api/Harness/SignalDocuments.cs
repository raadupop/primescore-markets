using System.Globalization;
using System.Text.Json.Nodes;

namespace PrimeScore.Acceptance.Api.Harness;

/// <summary>
/// Signal documents in the contract's <c>SignalInput</c> wire format, built as JSON so tests
/// can also send malformed variants the typed client cannot express.
/// </summary>
public static class SignalDocuments
{
    public static string Iso(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static JsonObject MarketData(string instrument, double value, DateTimeOffset observedAt, string source = "acceptance:market") => new()
    {
        ["source_category"] = "MARKET_DATA",
        ["source_identifier"] = source,
        ["timestamp"] = Iso(observedAt),
        ["payload_type"] = "STRUCTURED",
        ["structured_payload"] = new JsonObject
        {
            ["asset_class"] = "equity_index",
            ["instrument"] = instrument,
            ["metric_type"] = "IMPLIED_VOLATILITY",
            ["value"] = value,
            ["unit"] = "points",
            ["observed_at"] = Iso(observedAt),
        },
    };

    public static JsonObject Macroeconomic(string source, double value, DateTimeOffset releasedAt) => new()
    {
        ["source_category"] = "MACROECONOMIC",
        ["source_identifier"] = source,
        ["timestamp"] = Iso(releasedAt),
        ["payload_type"] = "STRUCTURED",
        ["structured_payload"] = new JsonObject
        {
            ["indicator_type"] = "INFLATION",
            ["region"] = "US",
            ["value"] = value,
            ["release_date"] = Iso(releasedAt),
        },
    };

    public static JsonObject Geopolitical(string region, double severityEstimate, DateTimeOffset observedAt) => new()
    {
        ["source_category"] = "GEOPOLITICAL",
        ["source_identifier"] = "curated:acceptance",
        ["timestamp"] = Iso(observedAt),
        ["payload_type"] = "STRUCTURED",
        ["structured_payload"] = new JsonObject
        {
            ["event_region"] = region,
            ["event_type"] = "military_strike",
            ["severity_estimate"] = severityEstimate,
            ["actors"] = new JsonArray("A", "B"),
        },
    };

    public static JsonObject CrossAssetFlow(string pair, double value, double baseline, DateTimeOffset observedAt) => new()
    {
        ["source_category"] = "CROSS_ASSET_FLOW",
        ["source_identifier"] = "acceptance:flows",
        ["timestamp"] = Iso(observedAt),
        ["payload_type"] = "STRUCTURED",
        ["structured_payload"] = new JsonObject
        {
            ["flow_type"] = "CORRELATION_BREAKDOWN",
            ["asset_pair"] = pair,
            ["value"] = value,
            ["baseline_value"] = baseline,
            ["lookback_period"] = "90d",
        },
    };

    public static JsonObject Batch(params JsonNode[] signals) => new() { ["signals"] = new JsonArray(signals) };
}
