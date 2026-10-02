using PrimeScore.Modules.Analytics.Features;
using PrimeScore.Modules.Decision.Contracts;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Analytics.Tests;

/// <summary>The outcomes record (ADR-0008) on hand-built closes; every expectation is worked out in the comments.</summary>
public sealed class ForwardOutcomesTests
{
    private static readonly DateOnly Start = new(2024, 1, 2);
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

    /// <summary>30 NYSE trading days of closes 10, 11, 12, …, 39 (2024-01-15 is a holiday and has none): every h-day change is +h.</summary>
    private static List<(DateTimeOffset, double)> RisingCloses() =>
        Enumerable.Range(0, 30).Select(day => (Close(day), 10.0 + day)).ToList();

    private static DateTimeOffset Close(int tradingDay) =>
        MarketTime.AtNewYork(MarketTime.AddTradingDays(Start, tradingDay), new TimeOnly(16, 15));

    private static DecisionOutcomePoint Point(int tradingDay, DecisionOutcome outcome, double composite, string state,
        long sequence = 1, int minutes = 0, string instrument = "VIX") =>
        new(Close(tradingDay).AddMinutes(minutes), sequence, outcome, composite, state, instrument);

    [Fact]
    public void After_an_extreme_high_a_rise_is_not_a_reversion_and_after_an_extreme_low_it_is()
    {
        // Day 0 DEPLOY extreme_high (rises +h: not reverted), day 1 DEPLOY extreme_low (rises +h: reverted), day 2 IDLE.
        var report = GetForwardOutcomesHandler.Compute("equity", "VIX",
            [Point(0, DecisionOutcome.Deploy, 0.9, "extreme_high"), Point(1, DecisionOutcome.Deploy, -0.9, "extreme_low"), Point(2, DecisionOutcome.Idle, 0.1, "normal")],
            RisingCloses(), Now);

        Assert.Equal((3, 2, 0, 0, Start, MarketTime.AddTradingDays(Start, 2)),
            (report.Days, report.DeployDays, report.LegacyDays, report.ExcludedDays, report.FirstDate, report.LastDate));
        var five = report.Horizons.Single(row => row.TradingDays == 5);
        Assert.Equal((2, 3, 5.0, 5.0, 0.5, 1.0), (five.DeployCount, five.AllCount, five.DeployMedianAbsChange!.Value, five.AllMedianAbsChange!.Value, five.DeployRevertShare!.Value, five.AllUpShare!.Value));
        // Toward the median: −5 after the high, +5 after the low; mean 0.
        Assert.Equal(0, five.DeployMeanChangeTowardMedian!.Value, 12);
        Assert.Null(five.DeployRevertShareLow);
    }

    [Fact]
    public void A_horizon_past_the_last_close_is_left_out_and_reported_as_null_not_zero()
    {
        // Day 20 of 0–29 has 9 later closes: its 1- and 5-day changes exist, its 10- and 21-day changes do not.
        var report = GetForwardOutcomesHandler.Compute("equity", "VIX", [Point(20, DecisionOutcome.Deploy, 0.5, "extreme_high")], RisingCloses(), Now);

        Assert.Equal([1, 1, 0, 0], report.Horizons.Select(row => row.AllCount));
        Assert.Equal([1, 1, 0, 0], report.Horizons.Select(row => row.DeployCount));
        var month = report.Horizons.Single(row => row.TradingDays == 21);
        Assert.Equal((double?)null, month.AllMedianAbsChange);
        Assert.Equal((double?)null, month.AllUpShare);
        Assert.Null(month.DeployRevertShare);
        Assert.Null(report.States.Single().MedianChange21);
    }

    [Fact]
    public void The_last_decision_of_a_date_is_its_state_and_a_date_without_a_trading_day_close_is_excluded_and_counted()
    {
        // Two decisions on day 3 (IDLE then DEPLOY, later sequence wins); a decision on a Saturday has no close.
        var saturday = new DecisionOutcomePoint(MarketTime.AtNewYork(new DateOnly(2024, 1, 6), new TimeOnly(12, 0)), 9, DecisionOutcome.Deploy, 0.9, "extreme_high", "VIX");
        var report = GetForwardOutcomesHandler.Compute("equity", "VIX",
            [Point(3, DecisionOutcome.Idle, 0.2, "normal", sequence: 1), Point(3, DecisionOutcome.Deploy, 0.8, "extreme_high", sequence: 2, minutes: 1), saturday],
            RisingCloses(), Now);

        Assert.Equal((1, 1, 1), (report.Days, report.DeployDays, report.ExcludedDays));
        Assert.Equal(["extreme_high"], report.States.Select(state => state.State));
    }

    [Fact]
    public void A_holiday_session_close_is_neither_a_base_nor_a_target()
    {
        // 2024-01-15 (Martin Luther King Jr. Day) gets a print of 99, as Cboe sometimes publishes. Day 8 is 2024-01-12;
        // one trading day later is 2024-01-16 (close 19), so the change is 19 − 18 = +1, not 99 − 18.
        var closes = RisingCloses();
        closes.Add((MarketTime.AtNewYork(new DateOnly(2024, 1, 15), new TimeOnly(16, 15)), 99));
        var onHoliday = new DecisionOutcomePoint(MarketTime.AtNewYork(new DateOnly(2024, 1, 15), new TimeOnly(16, 15)), 5, DecisionOutcome.Idle, 0.9, "high", "VIX");

        var report = GetForwardOutcomesHandler.Compute("equity", "VIX", [Point(8, DecisionOutcome.Idle, 0.1, "normal"), onHoliday], closes, Now);

        Assert.Equal(new DateOnly(2024, 1, 12), MarketTime.AddTradingDays(Start, 8));
        Assert.Equal((1, 1), (report.Days, report.ExcludedDays));
        Assert.Equal(1.0, report.Horizons.Single(row => row.TradingDays == 1).AllMedianAbsChange!.Value, 12);
    }

    [Fact]
    public void Legacy_labels_and_other_instruments_never_count_as_extreme_state_days()
    {
        // A pre-ADR-0008 DEPLOY (vol-expansion) is legacy; a decision placed on VXN is not measured on VIX closes.
        var report = GetForwardOutcomesHandler.Compute("equity", "VIX",
            [Point(0, DecisionOutcome.Deploy, 0.9, "vol-expansion"), Point(1, DecisionOutcome.Deploy, 0.9, "extreme_high", instrument: "VXN")],
            RisingCloses(), Now);

        Assert.Equal((1, 0, 1, 0, 1), (report.Days, report.DeployDays, report.LegacyDays, report.ExcludedDays, report.OtherInstrumentDays));
        Assert.Equal(0, report.Horizons.Single(row => row.TradingDays == 5).DeployCount);
        Assert.Equal(["vol-expansion"], report.States.Select(state => state.State));
    }

    [Fact]
    public void A_date_keeps_its_own_instrument_decision_when_a_later_one_that_day_used_another_instrument()
    {
        // Day 4: VIX decision, then a VXN decision after a mid-day reference change. The VIX one is measured.
        var report = GetForwardOutcomesHandler.Compute("equity", "VIX",
            [Point(4, DecisionOutcome.Deploy, 0.9, "extreme_high", sequence: 1), Point(4, DecisionOutcome.Idle, 0.1, "normal", sequence: 2, minutes: 5, instrument: "VXN")],
            RisingCloses(), Now);

        Assert.Equal((1, 1, 0), (report.Days, report.DeployDays, report.OtherInstrumentDays));
    }

    [Fact]
    public void States_report_median_changes_and_up_shares_at_5_and_21_days()
    {
        // Closes 10, 11, … : a state on day 0 and day 1 both see +5 after 5 days and +21 after 21 days.
        var report = GetForwardOutcomesHandler.Compute("equity", "VIX",
            [Point(0, DecisionOutcome.Idle, 0.3, "high"), Point(1, DecisionOutcome.Idle, 0.3, "high")], RisingCloses(), Now);

        var high = Assert.Single(report.States);
        Assert.Equal(("high", 2, 5.0, 1.0, 21.0, 1.0), (high.State, high.Count, high.MedianChange5!.Value, high.UpShare5!.Value, high.MedianChange21!.Value, high.UpShare21!.Value));
    }

    [Fact]
    public void The_block_bootstrap_needs_two_blocks_and_brackets_the_mean()
    {
        var values = Enumerable.Range(0, 100).Select(index => index % 4 == 0 ? 1.0 : 0.0).ToArray();

        var (low, high) = GetForwardOutcomesHandler.BlockBootstrap(values);
        var (none, _) = GetForwardOutcomesHandler.BlockBootstrap(values, block: 51);

        Assert.True(low <= 0.25 && 0.25 <= high, $"[{low}, {high}] should contain 0.25");
        Assert.Null(none);
    }

    [Fact]
    public void Medians_of_even_and_odd_counts()
    {
        Assert.Equal(2.5, GetForwardOutcomesHandler.Median([4, 1, 3, 2]));
        Assert.Equal(3, GetForwardOutcomesHandler.Median([5, 3, 1]));
    }
}
