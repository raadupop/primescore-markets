using System.Globalization;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Ingestion.Sources.Cboe;

/// <summary>
/// How one Cboe history file is recorded. <see cref="Reconstructed"/> is tri-state: true before
/// <paramref name="ReconstructedBefore"/> (Cboe back-calculated the value), false from
/// <paramref name="LiveFrom"/> (disseminated live), null in between or when neither date could be
/// verified from a Cboe primary source; a ledger marker cannot be corrected later, so unknown stays unknown.
/// </summary>
/// <param name="Tenor">For the 9-day, 3-month and 6-month indices; part of the variant (<c>IMPLIED_VOLATILITY:9D</c>), as for an API submission with a tenor.</param>
/// <param name="AssetClass">MARKET_DATA payload asset class; null for the SPX level.</param>
/// <param name="StampNewYork">Wall-clock time on the data date every close is stamped at.</param>
/// <param name="LiveStart">What the dates rest on, for the provenance note.</param>
internal sealed record CboeIndex(
    string Symbol,
    SourceCategory Category,
    string? Tenor,
    string? AssetClass,
    string Unit,
    TimeOnly StampNewYork,
    DateOnly? ReconstructedBefore,
    DateOnly? LiveFrom,
    string LiveStart)
{
    public string Variant => Category == SourceCategory.MarketData
        ? Tenor is null ? CboeIndexCatalog.ImpliedVolatility : $"{CboeIndexCatalog.ImpliedVolatility}:{Tenor}"
        : "basket_observation";

    public string SourceIdentifier => "cboe:" + Symbol;

    /// <summary>When and how late the file carries a close, for the series row and the provenance note.</summary>
    public string Timing => string.Create(
        CultureInfo.InvariantCulture,
        $"close, {StampNewYork:HH:mm} New York on the data date; Cboe updates the file between about 18:00 ET and two days later");

    public DateTimeOffset ObservedAt(DateOnly date) => MarketTime.AtNewYork(date, StampNewYork);

    public bool? Reconstructed(DateOnly date) =>
        date < ReconstructedBefore ? true
        : date >= LiveFrom ? false
        : null;
}

/// <summary>
/// The Cboe index history files the adapter reads (v3 design §4). Live-start dates checked on
/// 2026-09-29: VIX9D live 2013-10-01 and VXMT (now VIX6M) 2013-11-27 (Cboe press releases); the
/// current VIX methodology was introduced in 2003 (Cboe VIX historical-data page; the exact day is
/// not stated there, so 2003 rows are unmarked); VVIX has no dated launch on a Cboe primary page.
/// VIX3M, VXN, RVX, OVX, GVZ and SPX files start after their index's launch.
/// </summary>
internal static class CboeIndexCatalog
{
    public const string ImpliedVolatility = "IMPLIED_VOLATILITY";

    private static readonly DateOnly FileStartsLive = DateOnly.MinValue;

    /// <summary>
    /// Volatility indices are stamped 16:15 New York (design §4), when the SPX options behind VIX stop
    /// trading; SPX at the 16:00 equity close, the stamp of the FRED basket's SP500 closes that SPX
    /// continues (ADR-0009).
    /// </summary>
    private static readonly TimeOnly VolatilityClose = new(16, 15);

    private static readonly TimeOnly EquityClose = new(16, 0);

    private static readonly CboeIndex[] Indices =
    [
        new("VIX", SourceCategory.MarketData, null, "equity_index", "points", VolatilityClose, new(2003, 1, 1), new(2004, 1, 1), "the current methodology was introduced in 2003"),
        new("VIX9D", SourceCategory.MarketData, "9D", "equity_index", "points", VolatilityClose, new(2013, 10, 1), new(2013, 10, 1), "live from 2013-10-01"),
        new("VIX3M", SourceCategory.MarketData, "3M", "equity_index", "points", VolatilityClose, null, FileStartsLive, "file starts after launch"),
        new("VIX6M", SourceCategory.MarketData, "6M", "equity_index", "points", VolatilityClose, new(2013, 11, 27), new(2013, 11, 27), "live from 2013-11-27 (as VXMT)"),
        new("VVIX", SourceCategory.MarketData, null, "equity_index", "points", VolatilityClose, null, null, "live start unverified"),
        new("OVX", SourceCategory.MarketData, null, "commodity", "points", VolatilityClose, null, FileStartsLive, "file starts after launch"),
        new("GVZ", SourceCategory.MarketData, null, "commodity", "points", VolatilityClose, null, FileStartsLive, "file starts after launch"),
        new("VXN", SourceCategory.MarketData, null, "equity_index", "points", VolatilityClose, null, FileStartsLive, "file starts after launch"),
        new("RVX", SourceCategory.MarketData, null, "equity_index", "points", VolatilityClose, null, FileStartsLive, "file starts after launch"),
        new("SPX", SourceCategory.CrossAssetFlow, null, null, "index", EquityClose, null, FileStartsLive, "file starts after launch"),
    ];

    public static IReadOnlyList<string> DefaultSymbols { get; } = Array.AsReadOnly(Indices.Select(index => index.Symbol).ToArray());

    public static CboeIndex? Find(string symbol) =>
        Indices.FirstOrDefault(index => string.Equals(index.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
}
