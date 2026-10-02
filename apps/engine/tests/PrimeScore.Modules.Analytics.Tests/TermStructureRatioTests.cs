using PrimeScore.Modules.Analytics.Catalysts;
using PrimeScore.Modules.Catalysts.Contracts;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Analytics.Tests;

/// <summary>
/// R(d) = VIX9D(d) ÷ VIX(d) − 1 on hand-built closes (ADR-0012). VIX closes 20 on even days of the
/// month and 25 on odd days; VIX9D is written out by hand on the dates that matter and is VIX × 1.40
/// (ratio +0.40) everywhere else, so a join on the wrong date shows as +0.40 or as a 20/25 mismatch.
/// </summary>
public sealed class TermStructureRatioTests
{
    private static readonly DateOnly First = new(2026, 8, 3);
    private static readonly DateOnly Last = new(2026, 9, 15);

    /// <summary>The ten placebo read dates with their VIX9D closes: ratio r, VIX by parity, VIX9D = VIX × (1 + r).</summary>
    private static readonly (DateOnly Date, double NineDay, double Ratio)[] Placebo =
    [
        (new(2026, 8, 4), 16.0, -0.20),   // even: 20 × 0.80
        (new(2026, 8, 5), 21.25, -0.15),  // odd:  25 × 0.85
        (new(2026, 8, 6), 18.0, -0.10),   // even: 20 × 0.90
        (new(2026, 8, 7), 23.75, -0.05),  // odd:  25 × 0.95
        (new(2026, 8, 10), 19.5, -0.025), // even: 20 × 0.975
        (new(2026, 8, 11), 25.0, 0.0),    // odd:  25 × 1.00
        (new(2026, 8, 12), 20.5, 0.025),  // even: 20 × 1.025
        (new(2026, 8, 13), 26.25, 0.05),  // odd:  25 × 1.05
        (new(2026, 8, 14), 22.0, 0.10),   // even: 20 × 1.10
        (new(2026, 8, 17), 28.75, 0.15),  // odd:  25 × 1.15
    ];

    /// <summary>The as-of date: 2026-09-15 is odd, 25 × 0.975 = 24.375, r = −0.025 (the same double as 19.5 ÷ 20 − 1).</summary>
    private static readonly DateOnly AsOf = new(2026, 9, 15);

    private static DateTimeOffset Close(DateOnly date) => MarketTime.AtNewYork(date, new TimeOnly(16, 15));

    private static double Vix(DateOnly date) => date.Day % 2 == 0 ? 20.0 : 25.0;

    private static (List<(DateTimeOffset, double)> NineDay, List<(DateTimeOffset, double)> ThirtyDay) Closes(params DateOnly[] withoutNineDay)
    {
        var nineDay = new List<(DateTimeOffset, double)>();
        var thirtyDay = new List<(DateTimeOffset, double)>();
        for (var date = First; date <= Last; date = date.AddDays(1))
        {
            if (!MarketTime.IsTradingDay(date))
            {
                continue;
            }

            thirtyDay.Add((Close(date), Vix(date)));
            if (withoutNineDay.Contains(date))
            {
                continue;
            }

            var placebo = Placebo.Where(item => item.Date == date).ToArray();
            nineDay.Add((Close(date), date == AsOf ? 24.375 : placebo.Length == 1 ? placebo[0].NineDay : Vix(date) * 1.40));
        }

        return (nineDay, thirtyDay);
    }

    private static TermStructureRatio Ratio(params DateOnly[] withoutNineDay)
    {
        var (nineDay, thirtyDay) = Closes(withoutNineDay);
        return TermStructureRatio.FromCloses(nineDay, thirtyDay);
    }

    [Fact]
    public void The_current_ratio_ranks_by_the_share_of_placebo_values_at_or_below_it()
    {
        var ratio = Ratio();

        Assert.True(ratio.TryGet(AsOf, out var current));
        Assert.Equal(-0.025, current, 12);
        var (values, missing) = ratio.Baseline(Placebo.Select(item => item.Date));
        Assert.Equal(0, missing);
        Assert.Equal(Placebo.Select(item => item.Ratio), values, (expected, actual) => Math.Abs(expected - actual) < 1e-12);

        // At or below −0.025: −0.20, −0.15, −0.10, −0.05 and −0.025 itself (a tie counts) = 5 of 10.
        Assert.Equal(0.5, TermStructureRatio.Percentile(current, values));
    }

    [Fact]
    public void Nine_placebo_values_are_too_few_for_a_percentile()
    {
        var ratio = Ratio();
        var (values, _) = ratio.Baseline(Placebo.Take(9).Select(item => item.Date));

        Assert.Equal(9, values.Count);
        Assert.Null(TermStructureRatio.Percentile(-0.025, values));
    }

    [Fact]
    public void A_read_date_without_a_VIX9D_close_is_counted_as_missing_and_not_filled()
    {
        var ratio = Ratio(withoutNineDay: new DateOnly(2026, 8, 12));

        var (values, missing) = ratio.Baseline(Placebo.Select(item => item.Date));

        Assert.Equal((9, 1), (values.Count, missing));
        Assert.False(ratio.TryGet(new DateOnly(2026, 8, 12), out _));
    }

    [Fact]
    public void A_reading_counts_missing_closes_and_names_a_baseline_that_is_too_small()
    {
        // CPI on Wed 2026-09-16 (hypothetical dates), read Tue 09-15 (the day before, c = 1), past CPI Wed 09-02.
        // Candidates p/q: 08-12/08-11 (r 0), 08-19/08-18 (r +0.40), 08-26/08-25 (no VIX9D close: missing 1).
        var ratio = Ratio(withoutNineDay: new DateOnly(2026, 8, 25));
        var events = new[] { new ScheduledEvent(CatalystFamily.Cpi, new DateOnly(2026, 9, 2)), new ScheduledEvent(CatalystFamily.Cpi, new DateOnly(2026, 9, 16)) };

        var reading = ratio.Read("CPI-2026-09-16", CatalystFamily.Cpi, new DateOnly(2026, 9, 16), events);

        Assert.Equal(((DateOnly?)AsOf, (int?)1, true), (reading.AsOf, reading.CalendarDaysBefore, reading.DayBefore));
        Assert.Equal(-0.025, reading.Value!.Value, 12);
        Assert.Equal((2, 3, 0, 0, 1), (reading.BaselineCount, reading.PlaceboDays, reading.HaloExcluded, reading.NoSession, reading.MissingCloses));
        Assert.Null(reading.BaselinePercentile);
        Assert.Equal("fewer than 10 placebo days (n = 2)", reading.NoBaselineReason);
        Assert.Equal(["FOMC", "CPI", "NFP", "GDP", "PCE", "OPEC"], reading.UnscreenedFamilies);
    }

    [Fact]
    public void A_holiday_session_print_is_never_the_as_of_date()
    {
        // 2026-07-03 is Independence Day observed; a print stamped on it is ignored, so the reading before
        // Monday 07-06 is Thursday 07-02 (ratio 22 ÷ 20 − 1 = +0.10).
        var ratio = TermStructureRatio.FromCloses(
            [(Close(new DateOnly(2026, 7, 2)), 22.0), (Close(new DateOnly(2026, 7, 3)), 30.0)],
            [(Close(new DateOnly(2026, 7, 2)), 20.0), (Close(new DateOnly(2026, 7, 3)), 25.0)]);

        Assert.Equal(new DateOnly(2026, 7, 2), ratio.LatestBefore(new DateOnly(2026, 7, 6)));
        Assert.False(ratio.TryGet(new DateOnly(2026, 7, 3), out _));
    }

    [Fact]
    public void Closes_before_the_VIX9D_live_start_have_no_ratio()
    {
        // 2013-09-30 (Monday) precedes 2013-10-01; 2013-10-01 itself counts.
        var ratio = TermStructureRatio.FromCloses(
            [(Close(new DateOnly(2013, 9, 30)), 15.0), (Close(new DateOnly(2013, 10, 1)), 16.0)],
            [(Close(new DateOnly(2013, 9, 30)), 16.0), (Close(new DateOnly(2013, 10, 1)), 16.0)]);

        Assert.False(ratio.TryGet(new DateOnly(2013, 9, 30), out _));
        Assert.Null(ratio.LatestBefore(new DateOnly(2013, 10, 1)));
        Assert.Equal(new DateOnly(2013, 10, 1), ratio.LatestBefore(new DateOnly(2013, 10, 2)));
    }
}
