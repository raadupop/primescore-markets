using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Ingestion.Sources.Fred;

internal enum FredTiming
{
    /// <summary>Daily index close, stamped 16:15 America/New_York (Cboe close) on the observation date.</summary>
    DailyClose,

    /// <summary>Release series: each value as first published, stamped 08:30 America/New_York on its first release date.</summary>
    InitialRelease,

    /// <summary>Basket price, stamped 16:00 America/New_York on the observation date; FRED may publish it later.</summary>
    BasketObservation,
}

/// <param name="Kind">MARKET_DATA asset class, MACROECONOMIC indicator type, or the basket instrument.</param>
internal sealed record FredSeries(
    string SeriesId,
    string Instrument,
    SourceCategory Category,
    FredTiming Timing,
    bool Verified,
    bool DeriveYearOverYear,
    string Kind,
    string Unit)
{
    public string SourceIdentifier => "fred:" + SeriesId;

    public Uri SourceUrl => new($"https://fred.stlouisfed.org/series/{SeriesId}");

    public TimeOnly NewYorkTime => Timing switch
    {
        FredTiming.DailyClose => new TimeOnly(16, 15),
        FredTiming.InitialRelease => new TimeOnly(8, 30),
        _ => new TimeOnly(16, 0),
    };

    public string TimingDescription => Timing switch
    {
        FredTiming.DailyClose => "daily close, 16:15 New York on the observation date",
        FredTiming.InitialRelease => "first release, 08:30 New York on the release date",
        _ => "observation date 16:00 New York; publication may be later",
    };
}

/// <summary>
/// Which FRED series the engine pulls (brief §7): every registry MARKET_DATA symbol mapped to
/// FRED (unverified mappings included and labelled), the verified registry MACROECONOMIC
/// symbols mapped to FRED, and the configured cross-asset basket.
/// </summary>
internal static class FredSeriesCatalog
{
    private static readonly Dictionary<string, string> AssetClasses = new(StringComparer.Ordinal)
    {
        ["VIX"] = "equity_index",
        ["VXN"] = "equity_index",
        ["RVX"] = "equity_index",
        ["VVIX"] = "equity_index",
        ["OVX"] = "commodity",
        ["GVZ"] = "commodity",
        ["EVZ"] = "fx",
    };

    private static readonly Dictionary<string, (string Indicator, string Unit)> MacroKinds = new(StringComparer.Ordinal)
    {
        ["us_inflation_yoy"] = ("INFLATION", "percent_yoy"),
        ["us_labor_weekly"] = ("EMPLOYMENT", "claims"),
    };

    private static readonly Dictionary<string, string> BasketUnits = new(StringComparer.Ordinal)
    {
        ["SP500"] = "index",
        ["DGS10"] = "percent",
        ["DCOILWTICO"] = "usd_per_barrel",
        ["DEXUSEU"] = "usd_per_eur",
    };

    public static IReadOnlyList<FredSeries> Build(IndicatorRegistry registry, FredOptions options)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        var series = new List<FredSeries>();
        foreach (var symbol in registry.SymbolsWithProvider("fred"))
        {
            var mapping = symbol.Bootstrap!;
            switch (symbol.IndicatorClass.SourceCategory)
            {
                case SourceCategory.MarketData:
                    series.Add(new FredSeries(
                        mapping.SeriesId, symbol.Symbol, SourceCategory.MarketData, FredTiming.DailyClose, mapping.Verified,
                        DeriveYearOverYear: false, AssetClasses.GetValueOrDefault(symbol.Symbol, "volatility_index"), "points"));
                    break;
                case SourceCategory.Macroeconomic when mapping.Verified && MacroKinds.TryGetValue(symbol.IndicatorClass.Name, out var kind):
                    series.Add(new FredSeries(
                        mapping.SeriesId, symbol.Symbol, SourceCategory.Macroeconomic, FredTiming.InitialRelease, Verified: true,
                        DeriveYearOverYear: mapping.Derive == "pct_change_yoy", kind.Indicator, kind.Unit));
                    break;
            }
        }

        foreach (var id in options.BasketSeries.Distinct(StringComparer.Ordinal))
        {
            series.Add(new FredSeries(
                id, id, SourceCategory.CrossAssetFlow, FredTiming.BasketObservation, Verified: true,
                DeriveYearOverYear: false, id, BasketUnits.GetValueOrDefault(id, "level")));
        }

        return series;
    }
}
