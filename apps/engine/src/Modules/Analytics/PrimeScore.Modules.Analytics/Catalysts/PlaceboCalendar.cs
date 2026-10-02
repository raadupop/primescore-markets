using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Analytics.Catalysts;

/// <summary>A scheduled (not cancelled) catalyst of the current calendar, by its New York date.</summary>
internal sealed record ScheduledEvent(CatalystFamily Family, DateOnly Date);

/// <summary>
/// Which dates a catalyst's ratio is ranked against (ADR-0012): placebo days on the catalyst's
/// weekday one, two and three weeks before each past event of its family, each read the same
/// number of calendar days earlier as the catalyst's own reading, so the read date keeps the
/// as-of date's weekday and VIX9D's calendar-day horizon covers the same weekdays. Candidates whose
/// placebo day or read date is not an NYSE session, or lies within one trading day of a scheduled
/// event of a halo family, are left out and counted. The constants are fixed here and in the ADR.
/// </summary>
internal static class PlaceboCalendar
{
    public static readonly IReadOnlyList<int> WeekOffsets = [1, 2, 3];

    /// <summary>Trading days on each side of an event that belong to its halo.</summary>
    public const int HaloTradingDays = 1;

    /// <summary>
    /// Families whose events carry a halo. The weekly families are left out on purpose: a halo
    /// around every Wednesday and Thursday would remove whole weekdays from every baseline.
    /// </summary>
    public static readonly IReadOnlyList<CatalystFamily> HaloFamilies =
        [CatalystFamily.Fomc, CatalystFamily.Cpi, CatalystFamily.Nfp, CatalystFamily.Gdp, CatalystFamily.Pce, CatalystFamily.Opec];

    /// <summary>Families released every week: every same weekday is an event day, so they get no baseline.</summary>
    public static readonly IReadOnlyList<CatalystFamily> WeeklyFamilies = [CatalystFamily.Claims, CatalystFamily.Wpsr];

    /// <summary>Below this many placebo values the percentile is not shown.</summary>
    public const int MinimumBaseline = 10;

    /// <summary>VIX9D's live start (ADR-0009); earlier read dates would rank back-calculated values.</summary>
    public static readonly DateOnly BaselineStart = new(2013, 10, 1);

    public const string WeeklyReason = "weekly release: every same weekday is an event day";

    /// <summary>
    /// The placebo read dates for a catalyst of <paramref name="family"/> on <paramref name="eventDate"/>
    /// whose own ratio is read on <paramref name="asOf"/>. <paramref name="events"/> is the whole current
    /// calendar (scheduled events only).
    /// </summary>
    public static PlaceboSelection Select(CatalystFamily family, DateOnly eventDate, DateOnly asOf, IReadOnlyList<ScheduledEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (WeeklyFamilies.Contains(family))
        {
            return new PlaceboSelection([], 0, 0, 0, [], WeeklyReason);
        }

        var offset = eventDate.DayNumber - asOf.DayNumber;
        var past = events.Where(item => item.Family == family && item.Date <= asOf && item.Date != eventDate).Select(item => item.Date).Distinct().ToArray();
        var sameWeekday = past.Where(date => date.DayOfWeek == eventDate.DayOfWeek).ToArray();
        var halo = HaloDays(events);

        var noSession = 0;
        var haloExcluded = 0;
        var considered = new List<DateOnly>();
        var kept = new List<DateOnly>();
        foreach (var placebo in sameWeekday.SelectMany(date => WeekOffsets.Select(weeks => date.AddDays(-7 * weeks))).Distinct().Order())
        {
            var read = placebo.AddDays(-offset);
            if (read < BaselineStart)
            {
                continue;
            }

            if (!MarketTime.IsTradingDay(placebo) || !MarketTime.IsTradingDay(read))
            {
                noSession++;
                continue;
            }

            considered.Add(read);
            if (halo.Contains(placebo) || halo.Contains(read))
            {
                haloExcluded++;
                continue;
            }

            kept.Add(read);
        }

        // A halo family whose recorded calendar starts after the earliest read date could not screen it.
        IReadOnlyList<CatalystFamily> unscreened = [];
        if (considered.Count > 0)
        {
            var earliest = considered.Min();
            unscreened = HaloFamilies.Where(screening => !events.Any(item => item.Family == screening && item.Date <= earliest)).ToArray();
        }

        return new PlaceboSelection(kept, haloExcluded, noSession, past.Length - sameWeekday.Length, unscreened, null);
    }

    /// <summary>Every date from one trading day before to one trading day after each halo-family event, weekends between included.</summary>
    private static HashSet<DateOnly> HaloDays(IReadOnlyList<ScheduledEvent> events)
    {
        var days = new HashSet<DateOnly>();
        foreach (var item in events.Where(item => HaloFamilies.Contains(item.Family)))
        {
            var end = MarketTime.AddTradingDays(item.Date, HaloTradingDays);
            for (var day = MarketTime.AddTradingDays(item.Date, -HaloTradingDays); day <= end; day = day.AddDays(1))
            {
                days.Add(day);
            }
        }

        return days;
    }
}

/// <param name="ReadDates">Read dates of the placebo days kept, oldest first; each is a trading day on the as-of date's weekday.</param>
/// <param name="OtherWeekdayEvents">Past events of the family on another weekday than the catalyst; not used.</param>
/// <param name="NoBaselineReason">Set when the family gets no baseline at all.</param>
internal sealed record PlaceboSelection(
    IReadOnlyList<DateOnly> ReadDates,
    int HaloExcluded,
    int NoSession,
    int OtherWeekdayEvents,
    IReadOnlyList<CatalystFamily> UnscreenedFamilies,
    string? NoBaselineReason);
