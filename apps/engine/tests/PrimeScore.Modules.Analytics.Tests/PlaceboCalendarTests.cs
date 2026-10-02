using PrimeScore.Modules.Analytics.Catalysts;
using PrimeScore.Modules.Catalysts.Contracts;

namespace PrimeScore.Modules.Analytics.Tests;

/// <summary>
/// Placebo read dates (ADR-0012) on hand-listed calendars. Placebo day p = D' − 7w (w = 1, 2, 3) for
/// each past same-weekday event D'; read date q = p − (D − a) calendar days; halo = one NYSE trading
/// day either side of a FOMC, CPI, NFP, GDP, PCE or OPEC event, applied to p and to q.
/// </summary>
public sealed class PlaceboCalendarTests
{
    private static DateOnly Day(int year, int month, int day) => new(year, month, day);

    private static ScheduledEvent Event(CatalystFamily family, int month, int day, int year = 2026) => new(family, Day(year, month, day));

    [Fact]
    public void A_the_day_before_reading_leaves_out_a_candidate_inside_an_FOMC_halo_and_names_the_families_not_yet_on_the_calendar()
    {
        // CPI on Wed 2026-10-14, read Tue 10-13 (c = 1). Wednesday CPI events before 10-13: 08-12 and 06-10; 09-11 (Fri)
        // and 07-14 (Tue) are other weekdays (2). Candidates p/q: 08-05/08-04, 07-29/07-28, 07-22/07-21, 06-03/06-02,
        // 05-27/05-26 (Tuesday after Memorial Day 05-25, a session), 05-20/05-19. FOMC 07-29 has halo 07-28..07-30, so
        // 07-29/07-28 is left out (1). Earliest considered q = 05-19: FOMC 01-28 screens it; CPI (first 06-10), NFP
        // (first 06-05), GDP, PCE and OPEC (none) do not.
        var events = new[]
        {
            Event(CatalystFamily.Cpi, 10, 14), Event(CatalystFamily.Cpi, 9, 11), Event(CatalystFamily.Cpi, 8, 12),
            Event(CatalystFamily.Cpi, 7, 14), Event(CatalystFamily.Cpi, 6, 10),
            Event(CatalystFamily.Fomc, 1, 28), Event(CatalystFamily.Fomc, 7, 29), Event(CatalystFamily.Nfp, 6, 5),
        };

        var selection = PlaceboCalendar.Select(CatalystFamily.Cpi, Day(2026, 10, 14), Day(2026, 10, 13), events);

        Assert.Equal([Day(2026, 5, 19), Day(2026, 5, 26), Day(2026, 6, 2), Day(2026, 7, 21), Day(2026, 8, 4)], selection.ReadDates);
        Assert.All(selection.ReadDates, date => Assert.Equal(DayOfWeek.Tuesday, date.DayOfWeek));
        Assert.Equal((1, 0, 2), (selection.HaloExcluded, selection.NoSession, selection.OtherWeekdayEvents));
        Assert.Equal([CatalystFamily.Cpi, CatalystFamily.Nfp, CatalystFamily.Gdp, CatalystFamily.Pce, CatalystFamily.Opec], selection.UnscreenedFamilies);
        Assert.Null(selection.NoBaselineReason);
    }

    [Fact]
    public void B_a_read_date_inside_a_halo_excludes_the_candidate_although_its_placebo_day_is_outside()
    {
        // NFP on Fri 2026-10-02, read Thu 10-01 (c = 1). Friday NFP events 09-04 and 08-07. Candidates p/q: 08-28/08-27,
        // 08-21/08-20, 08-14/08-13, 07-31/07-30, 07-24/07-23, 07-17/07-16. p = 07-31 is two trading days after FOMC
        // 07-29 (halo 07-28..07-30) but q = 07-30 is inside: left out. NFP halos 08-06..08-10 and 09-03..09-08 touch none.
        var events = new[] { Event(CatalystFamily.Nfp, 9, 4), Event(CatalystFamily.Nfp, 8, 7), Event(CatalystFamily.Fomc, 7, 29) };

        var selection = PlaceboCalendar.Select(CatalystFamily.Nfp, Day(2026, 10, 2), Day(2026, 10, 1), events);

        Assert.Equal([Day(2026, 7, 16), Day(2026, 7, 23), Day(2026, 8, 13), Day(2026, 8, 20), Day(2026, 8, 27)], selection.ReadDates);
        Assert.All(selection.ReadDates, date => Assert.Equal(DayOfWeek.Thursday, date.DayOfWeek));
        Assert.Equal((1, 0), (selection.HaloExcluded, selection.NoSession));
    }

    [Fact]
    public void C_a_reading_weeks_before_counts_read_dates_without_a_session_and_keeps_the_as_of_weekday()
    {
        // FOMC on Wed 2026-10-28, read Fri 10-23 (c = 5). Past Wednesday FOMC events 07-29 and 06-17 (10-28 is the
        // catalyst itself). Candidates p/q: 07-22/07-17, 07-15/07-10, 07-08/07-03 (Independence Day observed, no
        // session: 1), 06-10/06-05 (the NFP day, inside its halo: 1), 06-03/05-29, 05-27/05-22. Kept 4, all Fridays.
        var events = new[]
        {
            Event(CatalystFamily.Fomc, 6, 17), Event(CatalystFamily.Fomc, 7, 29), Event(CatalystFamily.Fomc, 10, 28), Event(CatalystFamily.Nfp, 6, 5),
        };

        var selection = PlaceboCalendar.Select(CatalystFamily.Fomc, Day(2026, 10, 28), Day(2026, 10, 23), events);

        Assert.Equal([Day(2026, 5, 22), Day(2026, 5, 29), Day(2026, 7, 10), Day(2026, 7, 17)], selection.ReadDates);
        Assert.All(selection.ReadDates, date => Assert.Equal(DayOfWeek.Friday, date.DayOfWeek));
        Assert.Equal((1, 1), (selection.HaloExcluded, selection.NoSession));

        // Earliest considered q = 05-22, before the first FOMC (06-17) and NFP (06-05) events: every halo family is unscreened.
        Assert.Equal(PlaceboCalendar.HaloFamilies, selection.UnscreenedFamilies);
    }

    [Fact]
    public void A_weekly_family_gets_no_placebo_days_and_says_why()
    {
        var events = new[] { Event(CatalystFamily.Wpsr, 9, 23), Event(CatalystFamily.Wpsr, 9, 16), Event(CatalystFamily.Fomc, 9, 16) };

        var selection = PlaceboCalendar.Select(CatalystFamily.Wpsr, Day(2026, 9, 30), Day(2026, 9, 29), events);

        Assert.Empty(selection.ReadDates);
        Assert.Empty(selection.UnscreenedFamilies);
        Assert.Equal("weekly release: every same weekday is an event day", selection.NoBaselineReason);
    }

    [Fact]
    public void A_read_date_before_the_VIX9D_live_start_is_dropped_without_being_counted()
    {
        // CPI on Wed 2013-11-20, read Tue 11-19 (c = 1); past Wednesday CPI 2013-10-16. Candidates p/q: 10-09/10-08,
        // 10-02/10-01 (the live start itself, kept), 09-25/09-24 (before 2013-10-01: dropped, in no count).
        var events = new[] { Event(CatalystFamily.Cpi, 10, 16, year: 2013), Event(CatalystFamily.Cpi, 11, 20, year: 2013) };

        var selection = PlaceboCalendar.Select(CatalystFamily.Cpi, Day(2013, 11, 20), Day(2013, 11, 19), events);

        Assert.Equal([Day(2013, 10, 1), Day(2013, 10, 8)], selection.ReadDates);
        Assert.Equal((0, 0), (selection.HaloExcluded, selection.NoSession));
    }
}
