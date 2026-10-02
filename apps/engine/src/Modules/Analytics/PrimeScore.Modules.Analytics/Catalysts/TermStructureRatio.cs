using System.Globalization;
using PrimeScore.Modules.Analytics.Contracts;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Analytics.Catalysts;

/// <summary>
/// The 9-day/30-day ratio R(d) = VIX9D(d) ÷ VIX(d) − 1 for each NYSE trading day from VIX9D's
/// live start with both closes (ADR-0012). Holiday-session prints are ignored, as in the outcomes
/// record; a date without both closes has no ratio and is never filled. Readings rank the ratio on
/// the last date before a catalyst within its placebo baseline by the empirical distribution
/// function, the engine's level-percentile convention (ties count as at or below).
/// </summary>
internal sealed class TermStructureRatio
{
    private readonly SortedList<DateOnly, double> _ratios;

    private TermStructureRatio(SortedList<DateOnly, double> ratios) => _ratios = ratios;

    /// <summary>Ratio dates, oldest first.</summary>
    public IList<DateOnly> Dates => _ratios.Keys;

    /// <summary>
    /// Joins the two series on the New York date of each close. The first point of a date is kept
    /// (the observation series already keeps the earliest recorded); a non-positive VIX close has no ratio.
    /// </summary>
    public static TermStructureRatio FromCloses(
        IEnumerable<(DateTimeOffset ObservedAt, double Value)> nineDay,
        IEnumerable<(DateTimeOffset ObservedAt, double Value)> thirtyDay)
    {
        var thirty = ByTradingDay(thirtyDay);
        var ratios = new SortedList<DateOnly, double>();
        foreach (var (date, value) in ByTradingDay(nineDay).OrderBy(pair => pair.Key))
        {
            if (thirty.TryGetValue(date, out var denominator) && denominator > 0)
            {
                ratios.Add(date, (value / denominator) - 1);
            }
        }

        return new TermStructureRatio(ratios);
    }

    public bool TryGet(DateOnly date, out double ratio) => _ratios.TryGetValue(date, out ratio);

    /// <summary>The latest date strictly before <paramref name="date"/> with a ratio; null when there is none.</summary>
    public DateOnly? LatestBefore(DateOnly date)
    {
        var keys = _ratios.Keys;
        int low = 0, high = keys.Count - 1;
        DateOnly? found = null;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (keys[middle] < date)
            {
                found = keys[middle];
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    /// <summary>The ratios on <paramref name="readDates"/>; dates without one are counted, never filled.</summary>
    public (IReadOnlyList<double> Values, int Missing) Baseline(IEnumerable<DateOnly> readDates)
    {
        var values = new List<double>();
        var missing = 0;
        foreach (var date in readDates)
        {
            if (_ratios.TryGetValue(date, out var ratio))
            {
                values.Add(ratio);
            }
            else
            {
                missing++;
            }
        }

        return (values, missing);
    }

    /// <summary>Share of <paramref name="baseline"/> at or below <paramref name="value"/>; null below <see cref="PlaceboCalendar.MinimumBaseline"/> values.</summary>
    public static double? Percentile(double value, IReadOnlyCollection<double> baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return baseline.Count < PlaceboCalendar.MinimumBaseline ? null : baseline.Count(item => item <= value) / (double)baseline.Count;
    }

    /// <summary>The reading for one catalyst on New York date <paramref name="eventDate"/> against the current calendar.</summary>
    public CatalystRatioReading Read(string catalystId, CatalystFamily family, DateOnly eventDate, IReadOnlyList<ScheduledEvent> events)
    {
        if (LatestBefore(eventDate) is not { } asOf)
        {
            return new CatalystRatioReading(catalystId, null, null, null, false, null, 0, 0, 0, 0, 0, [], "no date with both closes");
        }

        var value = _ratios[asOf];
        var selection = PlaceboCalendar.Select(family, eventDate, asOf, events);
        var (baseline, missing) = Baseline(selection.ReadDates);
        var percentile = Percentile(value, baseline);
        var reason = selection.NoBaselineReason
            ?? (percentile is null
                ? string.Create(CultureInfo.InvariantCulture, $"fewer than {PlaceboCalendar.MinimumBaseline} placebo days (n = {baseline.Count})")
                : null);
        return new CatalystRatioReading(
            catalystId,
            asOf,
            value,
            eventDate.DayNumber - asOf.DayNumber,
            asOf == MarketTime.AddTradingDays(eventDate, -1),
            percentile,
            baseline.Count,
            selection.ReadDates.Count,
            selection.HaloExcluded,
            selection.NoSession,
            missing,
            selection.UnscreenedFamilies.Select(WireName).ToArray(),
            reason);
    }

    /// <summary>The family's wire name (<c>FOMC</c>, <c>CPI</c>, ...): the upper-case member name, as the Catalysts contract defines it.</summary>
    internal static string WireName(CatalystFamily family) => family.ToString().ToUpperInvariant();

    private static Dictionary<DateOnly, double> ByTradingDay(IEnumerable<(DateTimeOffset ObservedAt, double Value)> closes)
    {
        var byDate = new Dictionary<DateOnly, double>();
        foreach (var (observedAt, value) in closes)
        {
            var date = MarketTime.NewYorkDate(observedAt);
            if (date >= PlaceboCalendar.BaselineStart && MarketTime.IsTradingDay(date))
            {
                byDate.TryAdd(date, value);
            }
        }

        return byDate;
    }
}
