using PrimeScore.SharedKernel;
using PrimeScore.SharedKernel.Registry;

namespace PrimeScore.Modules.Ingestion.Sources.Fred;

/// <summary>A FRED series the engine recorded until FRED became a cross-check; its rows stay as recorded history.</summary>
internal sealed record FredSeries(string SeriesId, string Instrument, SourceCategory Category, bool Verified)
{
    public string SourceIdentifier => "fred:" + SeriesId;

    public Uri SourceUrl => FredSeriesCatalog.SeriesUrl(SeriesId);
}

/// <summary>One Cboe index compared with the FRED series that republishes it; an unknown FRED id surfaces as an item error naming it.</summary>
internal sealed record FredCrossCheckPair(string CboeSymbol, string FredSeriesId)
{
    public string CboeSourceIdentifier => "cboe:" + CboeSymbol;
}

/// <summary>
/// Which FRED series matter now that FRED records nothing (ADR-0009): the series recorded until
/// then, for status coverage of their historical rows, and the pairs the cross-check compares.
/// Pairs come from the registry's FRED bootstrap mappings for the configured Cboe symbols, plus the
/// Cboe indices whose FRED copy the registry does not map (VIX3M as <c>VXVCLS</c>, SPX as <c>SP500</c>).
/// </summary>
internal static class FredSeriesCatalog
{
    /// <summary>The cross-asset basket recorded from FRED until the cross-check replaced it (brief §7).</summary>
    private static readonly string[] HistoricalBasket = ["SP500", "DGS10", "DCOILWTICO", "DEXUSEU"];

    private static readonly Dictionary<string, string> MacroClasses = new(StringComparer.Ordinal)
    {
        ["us_inflation_yoy"] = "INFLATION",
        ["us_labor_weekly"] = "EMPLOYMENT",
    };

    private static readonly Dictionary<string, string> UnmappedCboeCopies = new(StringComparer.Ordinal)
    {
        ["VIX3M"] = "VXVCLS",
        ["SPX"] = "SP500",
    };

    public static Uri SeriesUrl(string seriesId) => new($"https://fred.stlouisfed.org/series/{seriesId}");

    /// <summary>
    /// The series recorded until 2026-09-29: every registry MARKET_DATA symbol mapped to FRED, the
    /// verified registry macro symbols mapped to FRED, and the fixed cross-asset basket.
    /// </summary>
    public static IReadOnlyList<FredSeries> Historical(IndicatorRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var series = new List<FredSeries>();
        foreach (var symbol in registry.SymbolsWithProvider("fred"))
        {
            var mapping = symbol.Bootstrap!;
            switch (symbol.IndicatorClass.SourceCategory)
            {
                case SourceCategory.MarketData:
                    series.Add(new FredSeries(mapping.SeriesId, symbol.Symbol, SourceCategory.MarketData, mapping.Verified));
                    break;
                case SourceCategory.Macroeconomic when mapping.Verified && MacroClasses.ContainsKey(symbol.IndicatorClass.Name):
                    series.Add(new FredSeries(mapping.SeriesId, symbol.Symbol, SourceCategory.Macroeconomic, Verified: true));
                    break;
            }
        }

        series.AddRange(HistoricalBasket.Select(id => new FredSeries(id, id, SourceCategory.CrossAssetFlow, Verified: true)));
        return series;
    }

    /// <summary>The configured Cboe symbols FRED also publishes, minus <paramref name="excluded"/> (any letter case).</summary>
    public static IReadOnlyList<FredCrossCheckPair> CrossCheckPairs(
        IndicatorRegistry registry, IEnumerable<string> cboeSymbols, IEnumerable<string> excluded)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var skip = excluded.Select(symbol => symbol.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pairs = new List<FredCrossCheckPair>();
        foreach (var symbol in cboeSymbols.Where(symbol => !skip.Contains(symbol)))
        {
            if (registry.TryGetSymbol(symbol, out var registered) && registered.Bootstrap is { Provider: "fred" } mapping)
            {
                pairs.Add(new FredCrossCheckPair(symbol, mapping.SeriesId));
            }
            else if (UnmappedCboeCopies.TryGetValue(symbol, out var seriesId))
            {
                pairs.Add(new FredCrossCheckPair(symbol, seriesId));
            }
        }

        return pairs;
    }
}
