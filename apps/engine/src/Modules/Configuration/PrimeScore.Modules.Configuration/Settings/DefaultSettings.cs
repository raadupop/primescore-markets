using PrimeScore.Modules.Configuration.Contracts;

namespace PrimeScore.Modules.Configuration.Settings;

/// <summary>
/// The values seeded on first start (brief §6, §9; ADR-0004). None is calibrated: every one
/// is shown as "uncalibrated default" until an operator changes it.
/// </summary>
internal static class DefaultSettings
{
    public const string Uncalibrated = "uncalibrated default";

    public static EngineSettings Create() => new(
        // The contract's example weights; relative only, since the composite divides by Σ w over present categories.
        new WeightingSettings(
            "srs_default",
            new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["MARKET_DATA"] = 0.3,
                ["MACROECONOMIC"] = 0.25,
                ["GEOPOLITICAL"] = 0.25,
                ["CROSS_ASSET_FLOW"] = 0.2,
            },
            Aggregation.MaxConfirmedWeighted),
        BypassPercentile: 0.999,
        CorroborationWindows: new Dictionary<string, WindowSpan>(StringComparer.Ordinal)
        {
            ["MARKET_DATA"] = WindowSpan.Days(2),
            ["CROSS_ASSET_FLOW"] = WindowSpan.Days(2),
            ["MACROECONOMIC"] = WindowSpan.Of(1800),
            ["GEOPOLITICAL"] = WindowSpan.Of(1800),
        },
        ReportingIntervals: new Dictionary<string, WindowSpan>(StringComparer.Ordinal)
        {
            ["MARKET_DATA"] = WindowSpan.Days(1),
            ["CROSS_ASSET_FLOW"] = WindowSpan.Days(1),
            ["MACROECONOMIC"] = WindowSpan.Of(1800),
            ["GEOPOLITICAL"] = WindowSpan.Of(1800),
        },
        DropoutSchedule: [new DropoutTier(300, 0.95), new DropoutTier(1800, 0.7), new DropoutTier(null, 0.5)],
        Contexts:
        [
            new ContextSettings(
                "equity",
                "VIX",
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["MARKET_DATA"] = ["VIX", "VXN", "RVX"],
                    ["MACROECONOMIC"] = ["CPI_YOY", "INITIAL_CLAIMS"],
                    ["CROSS_ASSET_FLOW"] = ["SP500", "DGS10", "DEXUSEU"],
                },
                DislocationThreshold: 1.5,
                new SensitivityMap(1.0, 0.75, 0.5),
                new RegimeRule(RegimeMode.Percentile, 0.30, 0.70, null, null)),
            new ContextSettings(
                "oil",
                "OVX",
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["MARKET_DATA"] = ["OVX"],
                    ["CROSS_ASSET_FLOW"] = ["DCOILWTICO", "DEXUSEU"],
                },
                DislocationThreshold: 3.0,
                new SensitivityMap(1.0, 0.75, 0.5),
                new RegimeRule(RegimeMode.Percentile, 0.30, 0.70, null, null)),
        ],
        Calibration: Uncalibrated);
}
