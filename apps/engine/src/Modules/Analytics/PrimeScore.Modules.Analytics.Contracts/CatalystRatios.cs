using PrimeScore.SharedKernel.Cqrs;

namespace PrimeScore.Modules.Analytics.Contracts;

/// <summary>
/// The 9-day/30-day ratio (VIX9D ÷ VIX − 1, the slope of the short end of the volatility term
/// structure) read before each given catalyst, ranked among the same ratio read on weekday-matched
/// placebo days before past events of its family (ADR-0012). Only Cboe-recorded closes are read.
/// Computed from the ledger on read; ids the calendar does not know are left out.
/// </summary>
public sealed record GetCatalystRatios(IReadOnlyList<string> CatalystIds) : IQuery<CatalystRatioList>;

/// <param name="Readings">One per known requested id, in request order.</param>
public sealed record CatalystRatioList(IReadOnlyList<CatalystRatioReading> Readings, DateTimeOffset ComputedAt);

/// <summary>A null member means "not available", never zero.</summary>
/// <param name="AsOf">The latest NYSE trading day before the catalyst's New York date with both closes; null when there is none.</param>
/// <param name="Value">VIX9D ÷ VIX − 1 on <paramref name="AsOf"/>.</param>
/// <param name="CalendarDaysBefore">Calendar days from <paramref name="AsOf"/> to the catalyst's date; every placebo value is read the same number of days before its placebo day.</param>
/// <param name="DayBefore"><paramref name="AsOf"/> is the trading day immediately before the catalyst.</param>
/// <param name="BaselinePercentile">Share of baseline values at or below <paramref name="Value"/> (ties count); null below the minimum baseline size.</param>
/// <param name="BaselineCount">Placebo values in the baseline (n).</param>
/// <param name="PlaceboDays">Candidate placebo days outside every event halo; those without both closes are counted in <paramref name="MissingCloses"/>.</param>
/// <param name="HaloExcluded">Candidates left out because the placebo day or its read date lies within one trading day of a scheduled event.</param>
/// <param name="NoSession">Candidates left out because the placebo day or its read date is not an NYSE trading day.</param>
/// <param name="MissingCloses">Placebo days without both closes on the read date; left out, never filled.</param>
/// <param name="UnscreenedFamilies">
/// Halo families (wire names such as <c>FOMC</c>) whose recorded calendar starts after the earliest
/// read date, so the halo could not screen the earliest candidates for them.
/// </param>
/// <param name="NoBaselineReason">Why <paramref name="BaselinePercentile"/> is null; null when it is present.</param>
public sealed record CatalystRatioReading(
    string CatalystId,
    DateOnly? AsOf,
    double? Value,
    int? CalendarDaysBefore,
    bool DayBefore,
    double? BaselinePercentile,
    int BaselineCount,
    int PlaceboDays,
    int HaloExcluded,
    int NoSession,
    int MissingCloses,
    IReadOnlyList<string> UnscreenedFamilies,
    string? NoBaselineReason);
