using PrimeScore.Modules.Classification.Aggregation;
using PrimeScore.SharedKernel;

namespace PrimeScore.Modules.Classification.Tests;

/// <summary>
/// CLS-002 and CLS-006 as resolved in ADR-0004, each expectation worked out by hand in the
/// test name or comment before the calculators existed. Inputs are synthetic arithmetic.
/// </summary>
public sealed class AggregationTests
{
    // Tuesday 2026-03-03 16:15 New York (EST) = 21:15 UTC; Monday is the previous trading day.
    private static readonly DateTimeOffset Tuesday = new(2026, 3, 3, 21, 15, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Monday = Tuesday.AddDays(-1);
    private static readonly DateTimeOffset Friday = Tuesday.AddDays(-4);
    private static readonly DateTimeOffset Thursday = Tuesday.AddDays(-5);

    private static readonly CompositeParameters Defaults = new(
        "test",
        new Dictionary<SourceCategory, double> { [SourceCategory.MarketData] = 0.3, [SourceCategory.Macroeconomic] = 0.25, [SourceCategory.CrossAssetFlow] = 0.2 },
        NetMax: true,
        BypassPercentile: 0.999,
        new Dictionary<SourceCategory, Span>
        {
            [SourceCategory.MarketData] = new(2, null),
            [SourceCategory.CrossAssetFlow] = new(2, null),
            [SourceCategory.Macroeconomic] = new(null, 1800),
        },
        new Dictionary<SourceCategory, Span>
        {
            [SourceCategory.MarketData] = new(1, null),
            [SourceCategory.CrossAssetFlow] = new(1, null),
            [SourceCategory.Macroeconomic] = new(null, 1800),
        },
        [(300, 0.95), (1800, 0.7), (null, 0.5)],
        [SourceCategory.MarketData, SourceCategory.Macroeconomic, SourceCategory.CrossAssetFlow]);

    [Fact]
    public void Two_corroborating_closes_0_6x0_9_and_0_2x1_give_net_max_0_54_and_market_data_alone_makes_the_composite_0_54()
    {
        // s_MD = max⁺ − max⁻ = max(0.54, 0.2) − 0 = 0.54; P = {MD}: 0.3 × 1 × 0.54 / 0.3 = 0.54.
        var result = Compute(Tuesday, Market("VIX", Tuesday, 0.6, 0.9), Market("VXN", Tuesday, 0.2, 1.0));

        Assert.Equal(0.54, result.Score, 10);
        Assert.Equal([SourceCategory.Macroeconomic, SourceCategory.CrossAssetFlow], result.Absent.Select(absent => absent.Category));
    }

    [Fact]
    public void Opposing_near_equal_signals_0_8_and_minus_0_78_cancel_to_0_02_which_is_far_below_either_alone()
    {
        var result = Compute(Tuesday, Market("VIX", Tuesday, 0.8, 1.0), Market("VXN", Tuesday, -0.78, 1.0));

        Assert.Equal(0.02, result.Score, 10);
    }

    [Fact]
    public void A_single_close_at_0_9992_passes_the_0_999_bypass_without_corroboration()
    {
        var result = Compute(Tuesday, Market("VIX", Tuesday, 0.9992, 1.0));

        Assert.Equal(0.9992, result.Score, 10);
        Assert.Single(Assert.Single(result.Contributing).Confirmed);
    }

    [Fact]
    public void A_single_close_at_0_95_is_unconfirmed_so_market_data_is_absent_and_the_composite_is_0()
    {
        var result = Compute(Tuesday, Market("VIX", Tuesday, 0.95, 1.0));

        Assert.Equal(0.0, result.Score);
        Assert.Contains("unconfirmed", Assert.Single(result.Absent, absent => absent.Category == SourceCategory.MarketData).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_window_holds_the_evaluation_day_and_the_previous_trading_day_so_Friday_confirms_Monday_but_Thursday_is_out()
    {
        Assert.True(CompositeCalculator.InWindow(Friday, Monday, new Span(2, null)));
        Assert.False(CompositeCalculator.InWindow(Thursday, Monday, new Span(2, null)));
        Assert.False(CompositeCalculator.InWindow(Tuesday, Monday, new Span(2, null)));
        Assert.Equal(0.5, Compute(Monday, Market("VIX", Monday, 0.5, 1.0), Market("VIX", Friday, 0.3, 1.0)).Score, 10);
    }

    [Fact]
    public void Across_Good_Friday_the_Thursday_close_confirms_Monday_and_Monday_is_not_overdue()
    {
        // 2025-04-17 (Thursday) and 2025-04-21 (Monday) are consecutive NYSE trading days.
        var thursday = new DateTimeOffset(2025, 4, 17, 20, 15, 0, TimeSpan.Zero);
        var monday = new DateTimeOffset(2025, 4, 21, 20, 15, 0, TimeSpan.Zero);

        var result = Compute(monday, Market("OVX", monday, 0.27, 0.37), Market("OVX", thursday, 0.5, 1.0));

        Assert.Equal(0.5, result.Score, 10);
        Assert.Equal(0.0, CompositeCalculator.Staleness(monday, thursday, new Span(1, null)));
    }

    [Theory]
    [InlineData("2026-04-03T20:15:00Z", "2026-04-06T20:15:00Z", true)]
    [InlineData("2026-04-03T20:15:00Z", "2026-04-07T20:15:00Z", false)]
    [InlineData("2026-04-04T15:00:00Z", "2026-04-06T20:15:00Z", true)]
    [InlineData("2026-04-04T15:00:00Z", "2026-04-07T20:15:00Z", false)]
    [InlineData("2026-04-06T04:00:00Z", "2026-04-07T20:15:00Z", true)]
    public void A_two_trading_day_window_is_the_evaluation_day_and_the_previous_trading_day_so_Good_Friday_or_Saturday_stamps_lapse_on_Tuesday(
        string observed, string at, bool inside)
    {
        // 2026-04-03 is Good Friday; Monday 04-06 is the trading day before Tuesday 04-07 (00:00 New York = 04:00 UTC in April).
        var window = new Span(2, null);

        Assert.Equal(inside, CompositeCalculator.InWindow(
            DateTimeOffset.Parse(observed, System.Globalization.CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture),
            window));
    }

    [Theory]
    [InlineData(1800, true)]
    [InlineData(1801, false)]
    public void The_macro_window_holds_a_print_for_exactly_1800_seconds(int seconds, bool inside)
    {
        var release = new DateTimeOffset(2026, 3, 3, 13, 30, 0, TimeSpan.Zero);

        Assert.Equal(inside, CompositeCalculator.InWindow(release, release.AddSeconds(seconds), new Span(null, 1800)));
    }

    [Fact]
    public void An_08_30_macro_print_is_out_of_the_16_15_close_composite_which_stays_0_54()
    {
        // 08:30 New York is 7 h 45 min before the close: far past the 1800 s macro window.
        var release = Tuesday.AddHours(-7).AddMinutes(-45);

        var result = Compute(Tuesday, Market("VIX", Tuesday, 0.6, 0.9), Market("VXN", Tuesday, 0.2, 1.0),
            Macro("CPI_YOY", release, -0.5, 0.8), Macro("INITIAL_CLAIMS", release, -0.1, 0.5));

        Assert.Equal(0.54, result.Score, 10);
        Assert.Equal("no classified signal in the window", Assert.Single(result.Absent, absent => absent.Category == SourceCategory.Macroeconomic).Reason);
    }

    [Fact]
    public void A_fallback_contributes_but_never_confirms_so_a_lone_real_close_beside_it_stays_unconfirmed()
    {
        var fallback = Market("VIX", Monday, 0.7, 1.0) with { IsFallback = true };

        var result = Compute(Tuesday, Market("VXN", Tuesday, 0.6, 1.0), fallback);

        // The fallback is confirmed by the real VXN close, contributing 0.7; VXN has no real partner and is dropped.
        var marketData = Assert.Single(result.Contributing);
        Assert.Equal([fallback.AssessmentId], marketData.Confirmed);
        Assert.Equal(0.7, result.Score, 10);
    }

    [Fact]
    public void A_certainty_0_close_neither_contributes_nor_confirms_so_a_lone_0_6_close_beside_it_stays_unconfirmed()
    {
        // An unknown-indicator or insufficient-history answer (score 0, certainty 0) is no corroboration.
        var result = Compute(Tuesday, Market("VIX", Tuesday, 0.6, 1.0), Market("VXN", Tuesday, 0.0, 0.0));

        Assert.Equal(0.0, result.Score);
        Assert.Contains("1 unconfirmed", Assert.Single(result.Absent, absent => absent.Category == SourceCategory.MarketData).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_category_with_only_certainty_0_assessments_is_absent_and_says_why()
    {
        var result = Compute(Tuesday, Market("VIX", Tuesday, 0.0, 0.0), Market("VIX", Monday, 0.0, 0.0));

        Assert.Contains("2 assessment(s) in the window with certainty 0", Assert.Single(result.Absent, absent => absent.Category == SourceCategory.MarketData).Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(60, 0.95)]
    [InlineData(600, 0.7)]
    [InlineData(3600, 0.5)]
    public void A_category_overdue_by_1_10_or_60_minutes_is_discounted_by_the_schedule(int overdueSeconds, double expected)
    {
        // Monday's close was due again Tuesday 16:15 New York; evaluate that many seconds later.
        var at = Tuesday.AddSeconds(overdueSeconds);
        var result = Compute(at, Market("VIX", Monday, 0.6, 1.0), Market("VXN", Monday, 0.4, 1.0));

        var marketData = Assert.Single(result.Contributing);
        Assert.Equal(overdueSeconds, marketData.StalenessSeconds, 6);
        Assert.Equal(expected, marketData.Discount);
        Assert.Equal(expected * 0.6, result.Score, 10);
    }

    [Fact]
    public void Two_categories_0_3x0_54_and_0_25x_minus_0_4_over_0_55_give_0_1127()
    {
        // (0.3 × 0.54 + 0.25 × (−0.4)) / (0.3 + 0.25) = (0.162 − 0.1) / 0.55 = 0.062 / 0.55 = 0.112727…
        var release = Tuesday.AddHours(-7).AddMinutes(-45);   // 13:30 UTC = 08:30 New York (EST)
        var result = Compute(release.AddMinutes(10),
            Market("VIX", Monday, 0.6, 0.9), Market("VXN", Monday, 0.2, 1.0),
            Macro("CPI_YOY", release, -0.5, 0.8), Macro("INITIAL_CLAIMS", release, -0.1, 0.5));

        Assert.Equal(0.062 / 0.55, result.Score, 10);
        Assert.Equal(0.3 * 0.54 / 0.55, result.Contributing.Single(category => category.Category == SourceCategory.MarketData).WeightedContribution, 10);
    }

    [Fact]
    public void Weighted_mean_of_0_54_and_0_2_is_0_37_where_net_max_gives_0_54()
    {
        var mean = CompositeCalculator.Compute(Tuesday, Defaults with { NetMax = false }, [Market("VIX", Tuesday, 0.6, 0.9), Market("VXN", Tuesday, 0.2, 1.0)], []);

        Assert.Equal(0.37, mean.Score, 10);
    }

    [Fact]
    public void An_expected_category_with_only_unclassifiable_signals_is_absent_with_their_reason()
    {
        var result = CompositeCalculator.Compute(Tuesday, Defaults, [Market("VIX", Tuesday, 0.9992, 1.0)],
            [new OutcomeInput(SourceCategory.CrossAssetFlow, Tuesday.AddMinutes(-15), "classifier route not in v1")]);

        Assert.Equal("classifier route not in v1 (1 signal(s))", result.Absent.Single(absent => absent.Category == SourceCategory.CrossAssetFlow).Reason);
        Assert.Equal("no classified signal in the window", result.Absent.Single(absent => absent.Category == SourceCategory.Macroeconomic).Reason);
    }

    [Fact]
    public void Volmageddon_composite_0_9992_at_VIX_37_32_above_1259_of_1260_prior_levels_is_high_regime_so_dislocation_is_18_645072()
    {
        // Percentile 1259/1260 > 0.70 → high → k = 0.5. SignalImpliedIV = 37.32 × (1 + 0.9992 × 0.5) = 55.965072.
        var history = Enumerable.Repeat(13.0, 1259).Append(40.74).ToArray();

        var result = DislocationCalculator.Compute(0.9992, 37.32, history, Percentile(threshold: 1.5));

        Assert.Equal(DislocationCalculator.High, result.Regime);
        Assert.Equal(1259.0 / 1260.0, result.RegimePercentile);
        Assert.Equal(0.5, result.SensitivityFactor);
        Assert.Equal(55.965072, result.SignalImpliedIv, 9);
        Assert.Equal(18.645072, result.DislocationValue, 9);
        Assert.True(result.ThresholdBreached);
    }

    [Fact]
    public void Low_vol_composite_minus_0_8849_at_VIX_9_19_below_every_prior_level_is_low_regime_so_dislocation_is_minus_8_132231()
    {
        // Percentile 0/1260 < 0.30 → low → k = 1.0. SignalImpliedIV = 9.19 × (1 − 0.8849) = 1.057769.
        var result = DislocationCalculator.Compute(-0.8849, 9.19, Enumerable.Repeat(13.89, 1260).ToArray(), Percentile(threshold: 1.5));

        Assert.Equal(DislocationCalculator.Low, result.Regime);
        Assert.Equal(1.057769, result.SignalImpliedIv, 9);
        Assert.Equal(-8.132231, result.DislocationValue, 9);
        Assert.True(result.ThresholdBreached);
    }

    [Theory]
    [InlineData(0.4, 2.0)]
    [InlineData(-0.4, -2.0)]
    public void A_composite_of_equal_magnitude_and_opposite_sign_flips_the_dislocation_20_x_plus_minus_0_4_x_0_25(double composite, double expected)
    {
        // Level mode: 20 lies between 15 and 25 → normal → k = 0.25 (a configured value). 20 × c × 0.25 = ±2.0.
        var parameters = new DislocationParameters(1.5, 1.0, 0.25, 0.5, PercentileMode: false, 0.3, 0.7, LowVolUpper: 15, HighVolLower: 25);

        var result = DislocationCalculator.Compute(composite, 20, [], parameters);

        Assert.Equal(expected, result.DislocationValue, 12);
        Assert.Equal(20 + expected, result.SignalImpliedIv, 12);
        Assert.Equal(DislocationCalculator.Normal, result.Regime);
    }

    [Fact]
    public void Level_mode_still_records_the_percentile_that_the_state_gate_reads()
    {
        // ADR-0008: 20 is above 3 of the 4 prior levels (10, 15, 18, 30): percentile 0.75, while level mode labels it normal.
        var parameters = new DislocationParameters(1.5, 1.0, 0.25, 0.5, PercentileMode: false, 0.3, 0.7, LowVolUpper: 15, HighVolLower: 25);

        var result = DislocationCalculator.Compute(0.4, 20, [10, 15, 18, 30], parameters);

        Assert.Equal((DislocationCalculator.Normal, (double?)0.75, 4), (result.Regime, result.RegimePercentile, result.RegimeHistory));
    }

    [Fact]
    public void Without_prior_levels_the_percentile_regime_is_normal_and_a_dislocation_of_0_90045_misses_a_1_5_threshold()
    {
        // 18 × 0.0667 × 0.75 = 1.2006 × 0.75 = 0.90045.
        var result = DislocationCalculator.Compute(0.0667, 18, [], Percentile(threshold: 1.5));

        Assert.Equal(DislocationCalculator.Normal, result.Regime);
        Assert.Null(result.RegimePercentile);
        Assert.Equal(0.90045, result.DislocationValue, 9);
        Assert.False(result.ThresholdBreached);
    }

    [Theory]
    [InlineData(5.0, "normal", 0.5, 0.75)]
    [InlineData(3.0, "normal", 0.3, 0.75)]
    [InlineData(7.0, "normal", 0.7, 0.75)]
    [InlineData(2.5, "low_vol", 0.2, 1.0)]
    [InlineData(8.0, "high_vol", 0.8, 0.5)]
    public void Percentile_regimes_are_strict_at_0_30_and_0_70_against_levels_1_to_10(double level, string regime, double percentile, double k)
    {
        var result = DislocationCalculator.Compute(0.4, level, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], Percentile(threshold: 1.5));

        Assert.Equal((regime, (double?)percentile, k), (result.Regime, result.RegimePercentile, result.SensitivityFactor));
        Assert.Equal(level * 0.4 * k, result.DislocationValue, 12);
    }

    [Fact]
    public void Ties_count_as_at_or_below_so_level_5_among_three_5s_and_seven_6s_is_percentile_0_30_and_normal()
    {
        var result = DislocationCalculator.Compute(0.4, 5, [5, 5, 5, 6, 6, 6, 6, 6, 6, 6], Percentile(threshold: 1.5));

        Assert.Equal(("normal", (double?)0.3), (result.Regime, result.RegimePercentile));
    }

    [Theory]
    [InlineData(10.0, "low_vol", 1.0)]
    [InlineData(15.0, "normal", 0.75)]
    [InlineData(25.0, "normal", 0.75)]
    [InlineData(30.0, "high_vol", 0.5)]
    public void Level_regimes_are_low_below_15_and_high_above_25(double level, string regime, double k)
    {
        var parameters = new DislocationParameters(1.5, 1.0, 0.75, 0.5, PercentileMode: false, 0.30, 0.70, 15, 25);

        var result = DislocationCalculator.Compute(0.4, level, [1, 2, 3], parameters);

        // Level boundaries label the regime; the percentile is still recorded for the state gate (ADR-0008): every level here is above 1, 2 and 3.
        Assert.Equal((regime, k, (double?)1.0), (result.Regime, result.SensitivityFactor, result.RegimePercentile));
    }

    [Fact]
    public void Configured_percentile_bands_are_the_ones_read_so_0_2_is_normal_with_bands_0_1_and_0_9()
    {
        var parameters = new DislocationParameters(1.5, 1.0, 0.75, 0.5, PercentileMode: true, 0.10, 0.90, null, null);

        Assert.Equal("normal", DislocationCalculator.Compute(0.4, 2.5, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10], parameters).Regime);
    }

    private static CompositeResult Compute(DateTimeOffset at, params AssessmentInput[] assessments) =>
        CompositeCalculator.Compute(at, Defaults, assessments, []);

    private static DislocationParameters Percentile(double threshold) =>
        new(threshold, 1.0, 0.75, 0.5, PercentileMode: true, 0.30, 0.70, null, null);

    private static AssessmentInput Market(string instrument, DateTimeOffset at, double score, double certainty) =>
        new(Guid.NewGuid(), Guid.NewGuid(), SourceCategory.MarketData, instrument, at, score, certainty, IsFallback: false);

    private static AssessmentInput Macro(string instrument, DateTimeOffset at, double score, double certainty) =>
        new(Guid.NewGuid(), Guid.NewGuid(), SourceCategory.Macroeconomic, instrument, at, score, certainty, IsFallback: false);
}
